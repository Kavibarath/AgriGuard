import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../app/agri_widgets.dart';
import '../../app/motion.dart';
import '../../app/theme.dart';
import '../cases/case_widgets.dart';
import 'orders.dart';

/// "My orders" (§8, Component C): what each approved prescription ordered, where to collect it,
/// how far the dealer has got, and the pickup code the dealer asks for at the counter.
class OrdersScreen extends ConsumerStatefulWidget {
  const OrdersScreen({super.key});

  @override
  ConsumerState<OrdersScreen> createState() => _OrdersScreenState();
}

class _OrdersScreenState extends ConsumerState<OrdersScreen> {
  Timer? _poll;

  @override
  void initState() {
    super.initState();
    // While an order is still with the dealer, the status changes without the farmer doing
    // anything; a slow poll keeps "Ready to collect" honest.
    _poll = Timer.periodic(const Duration(seconds: 30), (_) {
      if (ref.read(myOrdersProvider).value?.any((o) => o.status.open) ?? false) ref.invalidate(myOrdersProvider);
    });
  }

  @override
  void dispose() {
    _poll?.cancel();
    super.dispose();
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
                    child: OrderCard(order: orders[i]),
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
  const OrderCard({required this.order, super.key});

  final FarmerOrder order;

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
