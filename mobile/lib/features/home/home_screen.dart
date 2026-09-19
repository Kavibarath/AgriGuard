import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../auth/auth_controller.dart';
import '../auth/auth_models.dart';

/// Landing screen after sign-in. Feature entries are added by their owners (registry, cases,
/// prescriptions, harvest); until then each is a placeholder that says what it will become.
class HomeScreen extends ConsumerWidget {
  const HomeScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final user = ref.watch(currentUserProvider);
    if (user == null) return const SizedBox.shrink(); // router is already redirecting

    return Scaffold(
      appBar: AppBar(
        title: const Text('AgriGuard'),
        actions: [
          IconButton(
            tooltip: 'Sign out',
            icon: const Icon(Icons.logout),
            onPressed: () => ref.read(authControllerProvider.notifier).logout(),
          ),
        ],
      ),
      body: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          ListTile(
            leading: const CircleAvatar(child: Icon(Icons.person)),
            title: Text(user.fullName),
            subtitle: Text('${user.role.label} · ${user.email}'),
          ),
          const SizedBox(height: 8),
          if (user.role == UserRole.farmer) ...[
            const _FeatureTile(icon: Icons.landscape, title: 'My farms & plots', subtitle: 'Register plots and track crop cycles'),
            const _FeatureTile(icon: Icons.camera_alt, title: 'Report a crop problem', subtitle: 'Photo, location and symptoms'),
            const _FeatureTile(icon: Icons.receipt_long, title: 'Prescriptions', subtitle: 'Approved treatments and pickup'),
            const _FeatureTile(icon: Icons.local_shipping, title: 'Harvest collection', subtitle: 'Book a slot at a collection centre'),
          ] else if (user.role == UserRole.fieldAgronomist) ...[
            const _FeatureTile(icon: Icons.fact_check, title: 'Cases in my district', subtitle: 'Field triage and follow-up'),
          ] else
            const _FeatureTile(icon: Icons.desktop_windows, title: 'Use the web console', subtitle: 'Dealer and administrator tools are on the web app'),
        ],
      ),
    );
  }
}

class _FeatureTile extends StatelessWidget {
  const _FeatureTile({required this.icon, required this.title, required this.subtitle});

  final IconData icon;
  final String title;
  final String subtitle;

  @override
  Widget build(BuildContext context) {
    return Card(
      child: ListTile(
        leading: Icon(icon),
        title: Text(title),
        subtitle: Text(subtitle),
        trailing: const Icon(Icons.chevron_right),
        onTap: () => ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('$title is coming in the next build.')),
        ),
      ),
    );
  }
}
