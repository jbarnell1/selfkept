import 'package:flutter/material.dart';

import '../theme/tokens.dart';
import 'surface_card.dart';

/// Six moods, lowest to highest. Drawn faces, no emoji font needed.
enum Mood {
  low,
  heavy,
  flat,
  okay,
  good,
  bright;

  static Mood fromInt(int value) =>
      value >= 0 && value < values.length ? values[value] : okay;

  static Mood fromString(String? value) {
    final lower = value?.trim().toLowerCase();
    for (final m in values) {
      if (m.name == lower) return m;
    }
    return okay;
  }

  String get displayName => switch (this) {
    low => 'Low',
    heavy => 'Heavy',
    flat => 'Flat',
    okay => 'Okay',
    good => 'Good',
    bright => 'Bright',
  };

  /// Mouth curvature from a frown (-1) to a smile (1).
  double get curve => switch (this) {
    low => -1.0,
    heavy => -0.6,
    flat => 0.0,
    okay => 0.3,
    good => 0.65,
    bright => 1.0,
  };
}

/// The check-in entry point on Today: a row of six round mood buttons and a
/// one-line note with a Keep button. Today wires [onKeep] to the check-ins
/// provider, which says what to do when no mood is chosen.
class CheckInEntry extends StatefulWidget {
  final void Function(Mood? mood, String note) onKeep;

  const CheckInEntry({super.key, required this.onKeep});

  @override
  State<CheckInEntry> createState() => _CheckInEntryState();
}

class _CheckInEntryState extends State<CheckInEntry> {
  Mood? _mood;
  final _note = TextEditingController();

  @override
  void dispose() {
    _note.dispose();
    super.dispose();
  }

  void _keep() => widget.onKeep(_mood, _note.text.trim());

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return SurfaceCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text('How is today?', style: theme.textTheme.headlineSmall),
          const SizedBox(height: Space.x4),
          LayoutBuilder(
            builder: (context, constraints) {
              final size = (constraints.maxWidth - Space.x2 * 5) / 6;
              final diameter = size.clamp(40.0, 56.0);
              return Row(
                mainAxisAlignment: MainAxisAlignment.spaceBetween,
                children: [
                  for (final mood in Mood.values)
                    MoodButton(
                      mood: mood,
                      selected: _mood == mood,
                      diameter: diameter,
                      onTap: () =>
                          setState(() => _mood = _mood == mood ? null : mood),
                    ),
                ],
              );
            },
          ),
          const SizedBox(height: Space.x4),
          Row(
            children: [
              Expanded(
                child: TextField(
                  controller: _note,
                  maxLines: 1,
                  textInputAction: TextInputAction.done,
                  onSubmitted: (_) => _keep(),
                  decoration: const InputDecoration(
                    hintText: 'A line about today',
                  ),
                ),
              ),
              const SizedBox(width: Space.x3),
              FilledButton(onPressed: _keep, child: const Text('Keep')),
            ],
          ),
        ],
      ),
    );
  }
}

/// One round mood button with a simple drawn face.
class MoodButton extends StatelessWidget {
  final Mood mood;
  final bool selected;
  final double diameter;
  final VoidCallback onTap;

  const MoodButton({
    super.key,
    required this.mood,
    required this.selected,
    required this.diameter,
    required this.onTap,
  });

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final colors = AppColors.of(context);
    final fill = selected ? scheme.primary : colors.surfaceRaised;
    final stroke = selected ? scheme.onPrimary : scheme.onSurfaceVariant;
    return Semantics(
      button: true,
      selected: selected,
      label: mood.displayName,
      child: Tooltip(
        message: mood.displayName,
        waitDuration: const Duration(milliseconds: 600),
        child: Material(
          color: Colors.transparent,
          child: InkWell(
            onTap: onTap,
            customBorder: const CircleBorder(),
            child: AnimatedContainer(
              duration: Motion.short,
              curve: Motion.curve,
              width: diameter,
              height: diameter,
              decoration: BoxDecoration(
                color: fill,
                shape: BoxShape.circle,
                border: Border.all(
                  color: selected ? scheme.primary : colors.hairline,
                ),
              ),
              child: CustomPaint(
                painter: _FacePainter(curve: mood.curve, color: stroke),
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class _FacePainter extends CustomPainter {
  final double curve;
  final Color color;

  const _FacePainter({required this.curve, required this.color});

  @override
  void paint(Canvas canvas, Size size) {
    final paint = Paint()
      ..color = color
      ..style = PaintingStyle.stroke
      ..strokeWidth = size.width * 0.055
      ..strokeCap = StrokeCap.round;
    final w = size.width;
    final h = size.height;

    // Eyes.
    final eyeY = h * 0.4;
    final eyeDx = w * 0.17;
    final eyeR = w * 0.035;
    final dot = Paint()
      ..color = color
      ..style = PaintingStyle.fill;
    canvas.drawCircle(Offset(w / 2 - eyeDx, eyeY), eyeR, dot);
    canvas.drawCircle(Offset(w / 2 + eyeDx, eyeY), eyeR, dot);

    // Mouth: a quadratic curve whose control point moves with the mood.
    final mouthY = h * 0.64;
    final half = w * 0.2;
    final lift = curve * h * 0.14;
    final path = Path()
      ..moveTo(w / 2 - half, mouthY - lift * 0.25)
      ..quadraticBezierTo(
        w / 2,
        mouthY + lift,
        w / 2 + half,
        mouthY - lift * 0.25,
      );
    canvas.drawPath(path, paint);
  }

  @override
  bool shouldRepaint(_FacePainter old) =>
      old.curve != curve || old.color != color;
}
