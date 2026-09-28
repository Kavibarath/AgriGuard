import 'package:agriguard_mobile/features/cases/case_widgets.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('formatLkr', () {
    test('groups thousands', () {
      expect(formatLkr(9600), 'LKR 9,600');
      expect(formatLkr(1234567), 'LKR 1,234,567');
      expect(formatLkr(480), 'LKR 480');
    });
  });

  group('formatNumber', () {
    test('drops trailing zeros but keeps real decimals', () {
      expect(formatNumber(0.6), '0.6');
      expect(formatNumber(0.48), '0.48');
      expect(formatNumber(2.0), '2');
      expect(formatNumber(0.125), '0.125');
    });
  });

  group('formatDate', () {
    test('shows a UTC timestamp on the phone\'s own calendar', () {
      final utc = DateTime.utc(2026, 9, 27, 23, 15);
      final local = utc.toLocal();
      expect(
        formatDate(utc),
        '${local.year}-${local.month.toString().padLeft(2, '0')}-${local.day.toString().padLeft(2, '0')}',
      );
    });

    test('leaves a plain date alone', () {
      expect(formatDate(DateTime.parse('2026-09-27')), '2026-09-27');
    });
  });
}
