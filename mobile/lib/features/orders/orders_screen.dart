import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

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
                  padding: const EdgeInsets.all(24),
                  children: const [
                    Notice(message: 'No orders yet. When an agronomist approves a treatment for your crop, its order appears here.'),
                  ],
                )
              : ListView.separated(
                  padding: const EdgeInsets.all(16),
                  itemCount: orders.length,
                  separatorBuilder: (_, _) => const SizedBox(height: 12),
                  itemBuilder: (_, i) => OrderCard(order: orders[i]),
                ),
        ),
      ),
    );
  }
}

class OrderCard extends StatelessWidget {
  const OrderCard({required this.order, super.key});

  final FarmerOrder order;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final o = order;
    return Card(
      margin: EdgeInsets.zero,
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Expanded(child: Text(o.orderNo, style: theme.textTheme.titleMedium)),
                Chip(label: Text(o.status.label), visualDensity: VisualDensity.compact, side: BorderSide.none),
              ],
            ),
            if (o.prescriptionNo != null)
              Text('For prescription ${o.prescriptionNo}${o.sprayDate == null ? '' : ' · spray on ${formatDate(o.sprayDate!)}'}',
                  style: theme.textTheme.bodySmall),
            const SizedBox(height: 12),
            _Progress(order: o),
            if (o.pickupCode case final code? when o.status.open) ...[
              const SizedBox(height: 12),
              _PickupCode(code: code, ready: o.status == OrderStatus.packed),
            ],
            const SizedBox(height: 12),
            for (final l in o.lines)
              Text('${l.packs} × ${l.productName} (${formatNumber(l.quantity)} ${l.unitLabel})'),
            Text('Total ${formatLkr(o.totalAmount)}', style: const TextStyle(fontWeight: FontWeight.w600)),
            const Divider(height: 24),
            Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                const Icon(Icons.storefront_outlined, size: 20),
                const SizedBox(width: 8),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(o.shopName, style: const TextStyle(fontWeight: FontWeight.w500)),
                      if (o.shopAddress != null) Text(o.shopAddress!),
                      if (o.shopPhone != null) Text(o.shopPhone!),
                    ],
                  ),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }
}

/// Being packed → Ready to collect → Collected, each with when it happened.
class _Progress extends StatelessWidget {
  const _Progress({required this.order});

  final FarmerOrder order;

  @override
  Widget build(BuildContext context) {
    final steps = [
      ('Ordered', order.confirmedAt),
      ('Packed', order.packedAt),
      ('Collected', order.collectedAt),
    ];
    final scheme = Theme.of(context).colorScheme;
    return Row(
      children: [
        for (final (label, at) in steps)
          Expanded(
            child: Semantics(
              container: true,
              label: '$label: ${at == null ? 'not yet' : formatDate(at)}',
              excludeSemantics: true,
              child: Column(
                children: [
                  Icon(at == null ? Icons.radio_button_unchecked : Icons.check_circle, color: at == null ? scheme.outline : scheme.primary),
                  Text(label, style: Theme.of(context).textTheme.bodySmall),
                  Text(at == null ? '—' : formatDate(at), style: Theme.of(context).textTheme.bodySmall),
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
    return Container(
      width: double.infinity,
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(color: theme.colorScheme.primaryContainer, borderRadius: BorderRadius.circular(12)),
      child: Column(
        children: [
          Text(ready ? 'Ready to collect. Show this code at the counter:' : 'Pickup code (the dealer is still packing):',
              textAlign: TextAlign.center, style: TextStyle(color: theme.colorScheme.onPrimaryContainer)),
          const SizedBox(height: 4),
          Semantics(
            label: 'Pickup code ${code.split('').join(' ')}',
            excludeSemantics: true,
            child: Text(
              spaced,
              style: theme.textTheme.headlineMedium?.copyWith(
                color: theme.colorScheme.onPrimaryContainer,
                fontWeight: FontWeight.w700,
                letterSpacing: 4,
              ),
            ),
          ),
          Text('Only you have this code. Do not share it until you are at the shop.',
              textAlign: TextAlign.center, style: theme.textTheme.bodySmall?.copyWith(color: theme.colorScheme.onPrimaryContainer)),
        ],
      ),
    );
  }
}
