import 'dart:async';

/// Runs [action] at most once per [interval]. The first call runs at once;
/// later calls inside the interval collapse into one trailing call with the
/// latest value, so the final slider position is always sent.
class Throttle<T> {
  Throttle(this.interval, this.action);

  final Duration interval;
  final void Function(T value) action;

  final _clock = Stopwatch();
  Timer? _timer;
  late T _pending;
  bool _hasPending = false;

  void call(T value) {
    if (!_clock.isRunning || _clock.elapsed >= interval) {
      _cancelPending();
      _run(value);
      return;
    }
    _pending = value;
    _hasPending = true;
    _timer ??= Timer(interval - _clock.elapsed, flush);
  }

  /// Send a pending trailing value now.
  void flush() {
    _timer?.cancel();
    _timer = null;
    if (_hasPending) {
      _hasPending = false;
      _run(_pending);
    }
  }

  void _cancelPending() {
    _timer?.cancel();
    _timer = null;
    _hasPending = false;
  }

  void _run(T value) {
    _clock
      ..reset()
      ..start();
    action(value);
  }

  void dispose() => _cancelPending();
}
