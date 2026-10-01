import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../app/agri_widgets.dart';
import '../../app/theme.dart';
import '../../core/api/api_exception.dart';
import 'auth_controller.dart';

/// Hill-country terraces, the same photograph as the web console's login panel. One of the two
/// photos the phone carries (docs/design/IMAGE-CREDITS.md): photos cost bytes in the field.
const loginPhoto = 'assets/img/hill-terraces-portrait.webp';

class LoginScreen extends ConsumerStatefulWidget {
  const LoginScreen({super.key});

  @override
  ConsumerState<LoginScreen> createState() => _LoginScreenState();
}

class _LoginScreenState extends ConsumerState<LoginScreen> {
  final _formKey = GlobalKey<FormState>();
  final _email = TextEditingController();
  final _password = TextEditingController();
  bool _submitting = false;
  bool _obscure = true;
  String? _error;

  @override
  void dispose() {
    _email.dispose();
    _password.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    if (!_formKey.currentState!.validate()) return;
    setState(() {
      _submitting = true;
      _error = null;
    });

    try {
      await ref.read(authControllerProvider.notifier).login(_email.text, _password.text);
      // The router's redirect moves us to /home once the session appears.
    } on ApiException catch (e) {
      // The API never says which half was wrong; neither do we.
      setState(() => _error = e.message);
    } finally {
      if (mounted) setState(() => _submitting = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    // The photo runs under the status bar, so its icons turn white.
    return AnnotatedRegion<SystemUiOverlayStyle>(
      value: SystemUiOverlayStyle.light,
      child: Scaffold(
        body: LayoutBuilder(
          builder: (context, constraints) => SingleChildScrollView(
            child: ConstrainedBox(
              constraints: BoxConstraints(minHeight: constraints.maxHeight),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  // On a short screen the photo gives way, so Sign in stays above the fold.
                  _PhotoPanel(
                    height: constraints.maxHeight < 720
                        ? (constraints.maxHeight * 0.22).clamp(120.0, 200.0)
                        : (constraints.maxHeight * 0.36).clamp(200.0, 320.0),
                  ),
                  // The form sits in the lower part of the screen, where a thumb reaches.
                  Padding(
                    padding: const EdgeInsets.fromLTRB(24, 28, 24, 24),
                    child: Center(
                      child: ConstrainedBox(
                        constraints: const BoxConstraints(maxWidth: 420),
                        child: Form(
                          key: _formKey,
                          autovalidateMode: AutovalidateMode.onUserInteraction,
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.stretch,
                            children: [
                              Text('Sign in to your farm advisory', style: theme.textTheme.headlineSmall),
                              const SizedBox(height: 6),
                              Text(
                                'Report crop problems, follow the advice, and plan your harvest.',
                                style: theme.textTheme.bodyMedium?.copyWith(color: AgriColors.inkMuted),
                              ),
                              const SizedBox(height: 24),
                              if (_error != null) ...[
                                _ErrorBanner(message: _error!),
                                const SizedBox(height: 16),
                              ],
                              TextFormField(
                                controller: _email,
                                keyboardType: TextInputType.emailAddress,
                                autocorrect: false,
                                autofillHints: const [AutofillHints.username],
                                textInputAction: TextInputAction.next,
                                decoration: const InputDecoration(
                                  labelText: 'Email',
                                  prefixIcon: Icon(Icons.mail_outline),
                                ),
                                validator: (value) {
                                  final v = value?.trim() ?? '';
                                  if (v.isEmpty) return 'Enter your email';
                                  if (!v.contains('@') || !v.contains('.')) return 'Enter a valid email address';
                                  return null;
                                },
                              ),
                              const SizedBox(height: 16),
                              TextFormField(
                                controller: _password,
                                obscureText: _obscure,
                                autofillHints: const [AutofillHints.password],
                                textInputAction: TextInputAction.done,
                                onFieldSubmitted: (_) => _submitting ? null : _submit(),
                                decoration: InputDecoration(
                                  labelText: 'Password',
                                  prefixIcon: const Icon(Icons.lock_outline),
                                  suffixIcon: IconButton(
                                    tooltip: _obscure ? 'Show password' : 'Hide password',
                                    icon: Icon(_obscure ? Icons.visibility_off_outlined : Icons.visibility_outlined),
                                    onPressed: () => setState(() => _obscure = !_obscure),
                                  ),
                                ),
                                validator: (value) => (value == null || value.isEmpty) ? 'Enter your password' : null,
                              ),
                              const SizedBox(height: 28),
                              FilledButton(
                                onPressed: _submitting ? null : _submit,
                                style: FilledButton.styleFrom(minimumSize: const Size.fromHeight(56)),
                                child: _submitting ? const InlineSpinner(size: 22) : const Text('Sign in'),
                              ),
                            ],
                          ),
                        ),
                      ),
                    ),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}

/// The photograph with the product's name and one line of positioning, over a canopy scrim so
/// the words stay readable whatever part of the photo is behind them.
class _PhotoPanel extends StatelessWidget {
  const _PhotoPanel({required this.height});

  final double height;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final width = MediaQuery.sizeOf(context).width;
    return SizedBox(
      height: height + MediaQuery.paddingOf(context).top,
      child: Stack(
        fit: StackFit.expand,
        children: [
          ColoredBox(
            color: AgriColors.brand800,
            child: Image.asset(
              loginPhoto,
              fit: BoxFit.cover,
              // The hills and terraces; the people working the field stay out of the crop.
              alignment: const Alignment(0, -0.55),
              cacheWidth: (width * MediaQuery.devicePixelRatioOf(context)).round(),
              excludeFromSemantics: true,
              errorBuilder: (_, _, _) => const SizedBox.shrink(),
            ),
          ),
          const DecoratedBox(
            decoration: BoxDecoration(
              gradient: LinearGradient(
                begin: Alignment.topCenter,
                end: Alignment.bottomCenter,
                colors: [Color(0x66133025), Color(0x14133025), Color(0xCC133025)],
                stops: [0, 0.4, 1],
              ),
            ),
          ),
          SafeArea(
            bottom: false,
            child: Padding(
              padding: const EdgeInsets.fromLTRB(24, 16, 24, 20),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    children: [
                      const BrandMark(size: 36),
                      const SizedBox(width: 10),
                      Text('AgriGuard', style: theme.textTheme.titleLarge?.copyWith(color: Colors.white)),
                    ],
                  ),
                  // A short panel (a small phone, or landscape) keeps only the name.
                  if (height >= 180) ...[
                    const Spacer(),
                    Text(
                      'Crop advice you can trust: every treatment is checked against the safety rules and approved by an agronomist.',
                      maxLines: 4,
                      overflow: TextOverflow.ellipsis,
                      style: theme.textTheme.bodyLarge?.copyWith(color: Colors.white, fontWeight: FontWeight.w500, height: 1.4),
                    ),
                  ],
                ],
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class _ErrorBanner extends StatelessWidget {
  const _ErrorBanner({required this.message});

  final String message;

  @override
  Widget build(BuildContext context) {
    return Semantics(
      liveRegion: true,
      child: Container(
        padding: const EdgeInsets.fromLTRB(12, 12, 14, 12),
        decoration: BoxDecoration(
          color: AgriColors.danger50,
          borderRadius: BorderRadius.circular(10),
          border: Border.all(color: AgriColors.danger200),
        ),
        child: Row(
          children: [
            const Icon(Icons.report_outlined, color: AgriColors.danger800, size: 22),
            const SizedBox(width: 10),
            Expanded(child: Text(message, style: const TextStyle(color: AgriColors.danger800, fontSize: 16))),
          ],
        ),
      ),
    );
  }
}
