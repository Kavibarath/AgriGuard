import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../app/agri_widgets.dart';
import '../../app/motion.dart';
import '../../app/theme.dart';
import '../../core/api/api_exception.dart';
import '../cases/case_widgets.dart';
import 'orders.dart';

/// "My orders" (§8, Component C): what each approved prescription ordered, where to collect it,
/// how far the dealer has got, the pickup code the dealer asks for at the counter, and paying.
///
/// Paying by card: the app asks the server for the provider's checkout page and opens it in a
/// browser tab; the farmer types the card there, never in AgriGuard. When they come back to the
/// app, it asks the server to check with the provider, and only then shows the order as paid.
class OrdersScreen extends ConsumerStatefulWidget {
  const OrdersScreen({super.key});

  @override
  ConsumerState<OrdersScreen> createState() => _OrdersScreenState();
}

class _OrdersScreenState extends ConsumerState<OrdersScreen> with WidgetsBindingObserver {
  Timer? _poll;

  /// The order whose payment page is being opened, to show its button busy.
  String? _opening;

  /// Payment pages opened from this screen, by order, to check when the farmer comes back.
  final Map<String, String> _awaiting = {};

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    // While an order is still with the dealer, the status changes without the farmer doing
    // anything; a slow poll keeps "Ready to collect" honest.
    _poll = Timer.periodic(const Duration(seconds: 30), (_) {
      if (ref.read(myOrdersProvider).value?.any((o) => o.status.open) ?? false) ref.invalidate(myOrdersProvider);
    });
    // A payment page left open last time (the app was closed while paying) is checked once on arrival.
    Future.microtask(() async {
      final List<FarmerOrder> orders;
      try {
        orders = await ref.read(myOrdersProvider.future);
      } on Object {
        return;
      }
      if (mounted && orders.any((o) => o.openPaymentId != null)) await _checkPayments(announce: false);
    });
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    _poll?.cancel();
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    // Back from the payment page's browser tab.
    if (state == AppLifecycleState.resumed && _awaiting.isNotEmpty) _checkPayments();
  }

  Future<void> _pay(FarmerOrder order) async {
    final messenger = ScaffoldMessenger.of(context);
    setState(() => _opening = order.id);
    try {
      final checkout = await ref.read(orderRepositoryProvider).startCardPayment(order.id);
      _awaiting[order.id] = checkout.paymentId;
      final opened = await ref.read(checkoutLauncherProvider)(checkout.checkoutUrl);
      if (!opened) {
        _awaiting.remove(order.id);
        messenger.showSnackBar(const SnackBar(content: Text('The payment page could not be opened. Check that the phone has a web browser.')));
      }
    } on ApiException catch (e) {
      messenger.showSnackBar(SnackBar(content: Text(e.message)));
    } finally {
      if (mounted) setState(() => _opening = null);
    }
  }

  /// Asks the server to check each open payment with the provider, then reloads the orders.
  Future<void> _checkPayments({bool announce = true}) async {
    final messenger = ScaffoldMessenger.of(context);
    final repository = ref.read(orderRepositoryProvider);
    final open = <String, String>{
      for (final o in ref.read(myOrdersProvider).value ?? const <FarmerOrder>[])
        if (o.openPaymentId != null) o.id: o.openPaymentId!,
      ..._awaiting,
    };
    for (final entry in open.entries) {
      try {
        final payment = await repository.syncCardPayment(entry.key, entry.value);
        final openedHere = _awaiting.containsKey(entry.key);
        if (payment.latestAttemptStatus != 'Pending') _awaiting.remove(entry.key);
        if (!announce || !openedHere || !mounted) continue;
        if (payment.paid) {
          messenger.showSnackBar(SnackBar(content: Text('Payment received for ${payment.orderNo}. Thank you.')));
        } else if (payment.latestAttemptStatus == 'Expired') {
          messenger.showSnackBar(const SnackBar(content: Text('The payment page closed before you paid. Nothing was charged.')));
        }
      } on ApiException catch (e) {
        if (announce && mounted) messenger.showSnackBar(SnackBar(content: Text(e.message)));
      }
    }
    if (mounted) ref.invalidate(myOrdersProvider);
  }

  @override
  Widget build(BuildContext context) {
    final orders = ref.watch(myOrdersProvider);
    return Scaffold(
      appBar: AppBar(title: const Text('My orders')),
      body: RefreshIndicator(
        onRefresh: () => ref.refresh(myOrdersProvider.future),
        child: orders.when(
          loading: () => const Center(child: CircularProgressIndicator()),
          error: (error, _) => ErrorRetry(error: error, onRetry: () => ref.invalidate(myOrdersProvider)),
          data: (orders) => orders.isEmpty
              ? ListView(
                  children: const [
                    EmptyState(
                      earth: true,
                      title: 'Nothing ordered',
                      message: 'No orders yet. When an agronomist approves a treatment for your crop, its order appears here.',
                    ),
                  ],
                )
              : ListView.separated(
                  padding: const EdgeInsets.fromLTRB(16, 12, 16, 24),
                  itemCount: orders.length,
                  separatorBuilder: (_, _) => const SizedBox(height: 14),
                  itemBuilder: (_, i) => Reveal(
                    index: i,
                    child: OrderCard(order: orders[i], onPay: () => _pay(orders[i]), opening: _opening == orders[i].id),
                  ),
                ),
        ),
      ),
    );
  }
}

/// How each order status reads: still with the dealer, ready, done or called off.
Tone orderTone(OrderStatus status) => switch (status) {
  OrderStatus.confirmed => Tone.active,
  OrderStatus.packed || OrderStatus.collected => Tone.done,
  OrderStatus.cancelled => Tone.danger,
  OrderStatus.draft => Tone.neutral,
};

class OrderCard extends StatelessWidget {
  const OrderCard({required this.order, this.onPay, this.opening = false, super.key});

  final FarmerOrder order;

  /// Opens the card payment page; null where paying here is not offered.
  final VoidCallback? onPay;
  final bool opening;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final o = order;
    return AgriCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              const GlyphTile(Icons.shopping_bag_outlined, earth: true),
              const SizedBox(width: 12),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(o.orderNo, style: theme.textTheme.titleMedium),
                    if (o.prescriptionNo != null)
                      Text(
                        'For prescription ${o.prescriptionNo}${o.sprayDate == null ? '' : ' · spray on ${formatDate(o.sprayDate!)}'}',
                        style: theme.textTheme.bodySmall,
                      ),
                    const SizedBox(height: 8),
                    StatusPill(label: o.status.label, tone: orderTone(o.status)),
                  ],
                ),
              ),
            ],
          ),
          const SizedBox(height: 18),
          _Progress(order: o),
          if (o.paid || o.status.open) ...[
            const SizedBox(height: 16),
            _Payment(order: o, onPay: onPay, opening: opening),
          ],
          if (o.pickupCode case final code? when o.status.open) ...[
            const SizedBox(height: 16),
            _PickupCode(code: code, ready: o.status == OrderStatus.packed),
          ],
          const SizedBox(height: 16),
          Container(
            padding: const EdgeInsets.fromLTRB(12, 10, 12, 10),
            decoration: BoxDecoration(color: AgriColors.surfaceSunken, borderRadius: BorderRadius.circular(10)),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                for (final l in o.lines)
                  Padding(
                    padding: const EdgeInsets.only(bottom: 4),
                    child: Text('${l.packs} × ${l.productName} (${formatNumber(l.quantity)} ${l.unitLabel})', style: theme.textTheme.bodyMedium),
                  ),
                const Divider(height: 14),
                Text('Total ${formatLkr(o.totalAmount)}', textAlign: TextAlign.end, style: theme.textTheme.titleSmall),
              ],
            ),
          ),
          const SizedBox(height: 14),
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              const Padding(
                padding: EdgeInsets.only(top: 2),
                child: Icon(Icons.storefront_outlined, size: 22, color: AgriColors.earth600),
              ),
              const SizedBox(width: 10),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(o.shopName, style: theme.textTheme.titleSmall),
                    if (o.shopAddress != null) Text(o.shopAddress!, style: theme.textTheme.bodySmall),
                    if (o.shopPhone != null) Text(o.shopPhone!, style: theme.textTheme.bodySmall),
                  ],
                ),
              ),
            ],
          ),
        ],
      ),
    );
  }
}

/// Paid, and how; or what is owed, with the card button and the cash alternative.
class _Payment extends StatelessWidget {
  const _Payment({required this.order, required this.onPay, required this.opening});

  final FarmerOrder order;
  final VoidCallback? onPay;
  final bool opening;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final o = order;
    if (o.paid) {
      final how = o.paidByLabel;
      return Container(
        padding: const EdgeInsets.fromLTRB(12, 10, 12, 10),
        decoration: BoxDecoration(
          color: AgriColors.brand50,
          borderRadius: BorderRadius.circular(10),
          border: Border.all(color: AgriColors.brand300),
        ),
        child: Row(
          children: [
            const Icon(Icons.verified_outlined, color: AgriColors.brand700, size: 22),
            const SizedBox(width: 10),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text('Paid ${formatLkr(o.totalAmount)}', style: theme.textTheme.titleSmall?.copyWith(color: AgriColors.brand900)),
                  if (how != null) Text(o.paidAt == null ? how : '$how · ${formatDate(o.paidAt!)}', style: theme.textTheme.bodySmall),
                ],
              ),
            ),
          ],
        ),
      );
    }

    final continuing = o.openPaymentId != null;
    return Container(
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: AgriColors.surfaceSunken,
        borderRadius: BorderRadius.circular(10),
        border: Border.all(color: AgriColors.borderStrong),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              const Icon(Icons.payments_outlined, color: AgriColors.earth600, size: 22),
              const SizedBox(width: 10),
              Expanded(child: Text('To pay ${formatLkr(o.totalAmount)}', style: theme.textTheme.titleSmall)),
              const StatusPill(label: 'Unpaid', tone: Tone.warning),
            ],
          ),
          if (o.canPayByCard && onPay != null) ...[
            const SizedBox(height: 12),
            FilledButton.icon(
              onPressed: opening ? null : onPay,
              icon: opening
                  ? const SizedBox(width: 18, height: 18, child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white))
                  : const Icon(Icons.credit_card),
              label: Text(continuing ? 'Continue paying by card' : 'Pay by card'),
            ),
            const SizedBox(height: 8),
            Text(
              'Visa or Mastercard, on a secure payment page. Your card number is never stored in AgriGuard. Or pay in cash when you collect.',
              style: theme.textTheme.bodySmall,
            ),
          ] else ...[
            const SizedBox(height: 6),
            Text('Pay in cash at the shop when you collect. The dealer hands it over once it is paid.', style: theme.textTheme.bodySmall),
          ],
        ],
      ),
    );
  }
}

/// Ordered → Packed → Collected as three joined nodes, each with when it happened.
class _Progress extends StatelessWidget {
  const _Progress({required this.order});

  final FarmerOrder order;

  @override
  Widget build(BuildContext context) {
    final steps = [('Ordered', order.confirmedAt), ('Packed', order.packedAt), ('Collected', order.collectedAt)];
    final theme = Theme.of(context);
    return Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        for (final (i, (label, at)) in steps.indexed)
          Expanded(
            child: Semantics(
              container: true,
              label: '$label: ${at == null ? 'not yet' : formatDate(at)}',
              excludeSemantics: true,
              child: Column(
                children: [
                  Row(
                    children: [
                      // The line in from the step before, and out to the step after.
                      Expanded(
                        child: Container(
                          height: 2,
                          color: i == 0 ? Colors.transparent : (at == null ? AgriColors.borderStrong : AgriColors.brand300),
                        ),
                      ),
                      at == null
                          ? const ToneIcon(Tone.neutral, size: 26, color: AgriColors.outline)
                          : Container(
                              width: 26,
                              height: 26,
                              decoration: const BoxDecoration(color: AgriColors.brand600, shape: BoxShape.circle),
                              child: const Icon(Icons.check, size: 16, color: Colors.white),
                            ),
                      Expanded(
                        child: Container(
                          height: 2,
                          color: i == steps.length - 1
                              ? Colors.transparent
                              : (steps[i + 1].$2 == null ? AgriColors.borderStrong : AgriColors.brand300),
                        ),
                      ),
                    ],
                  ),
                  const SizedBox(height: 6),
                  Text(
                    label,
                    style: theme.textTheme.bodySmall?.copyWith(fontWeight: FontWeight.w600, color: at == null ? AgriColors.inkMuted : AgriColors.ink),
                  ),
                  Text(at == null ? '—' : formatDate(at), style: theme.textTheme.bodySmall),
                ],
              ),
            ),
          ),
      ],
    );
  }
}

/// The code the dealer asks for, large enough to read across a counter.
class _PickupCode extends StatelessWidget {
  const _PickupCode({required this.code, required this.ready});

  final String code;
  final bool ready;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final spaced = code.length == 6 ? '${code.substring(0, 3)} ${code.substring(3)}' : code;
    final ink = ready ? AgriColors.brand900 : AgriColors.inkSoft;
    return Container(
      width: double.infinity,
      padding: const EdgeInsets.fromLTRB(14, 14, 14, 12),
      decoration: BoxDecoration(
        color: ready ? AgriColors.brand50 : AgriColors.surfaceSunken,
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: ready ? AgriColors.brand600 : AgriColors.borderStrong, width: ready ? 2 : 1),
      ),
      child: Column(
        children: [
          Row(
            mainAxisAlignment: MainAxisAlignment.center,
            children: [
              Icon(ready ? Icons.check_circle_outline : Icons.schedule, size: 20, color: ink),
              const SizedBox(width: 6),
              Flexible(
                child: Text(
                  ready ? 'Ready to collect. Show this code at the counter:' : 'Pickup code (the dealer is still packing):',
                  textAlign: TextAlign.center,
                  style: TextStyle(color: ink, fontSize: 15, fontWeight: FontWeight.w600),
                ),
              ),
            ],
          ),
          const SizedBox(height: 6),
          Semantics(
            label: 'Pickup code ${code.split('').join(' ')}',
            excludeSemantics: true,
            child: Text(
              spaced,
              style: theme.textTheme.displaySmall?.copyWith(
                color: ink,
                fontWeight: FontWeight.w700,
                letterSpacing: 6,
                fontFeatures: const [FontFeature.tabularFigures()],
              ),
            ),
          ),
          const SizedBox(height: 4),
          Text(
            'Only you have this code. Do not share it until you are at the shop.',
            textAlign: TextAlign.center,
            style: theme.textTheme.bodySmall?.copyWith(color: ink),
          ),
        ],
      ),
    );
  }
}
