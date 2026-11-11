import 'dart:convert';

/// One server-sent event: its name (`message` when the frame named none) and
/// its data, with the lines of a multi-line `data:` joined by line breaks.
class SseEvent {
  final String event;
  final String data;

  const SseEvent(this.event, this.data);

  @override
  String toString() => 'SseEvent($event, $data)';
}

/// An incremental `text/event-stream` parser. Text arrives in pieces that
/// can split a line, or a CR LF pair, anywhere; [add] returns the events the
/// piece completed. `id` and `retry` fields are read past: nothing here
/// reconnects. An event cut off without its blank line is dropped, as the
/// format says.
class SseParser {
  final StringBuffer _line = StringBuffer();
  final List<String> _data = [];
  String _event = '';
  bool _afterCr = false;

  List<SseEvent> add(String chunk) {
    final out = <SseEvent>[];
    for (var i = 0; i < chunk.length; i++) {
      final c = chunk[i];
      if (_afterCr) {
        _afterCr = false;
        // The LF of a CR LF pair: the line already ended at the CR.
        if (c == '\n') continue;
      }
      if (c == '\r') {
        _afterCr = true;
        _endLine(out);
      } else if (c == '\n') {
        _endLine(out);
      } else {
        _line.write(c);
      }
    }
    return out;
  }

  void _endLine(List<SseEvent> out) {
    final line = _line.toString();
    _line.clear();
    if (line.isEmpty) {
      if (_data.isNotEmpty) {
        out.add(
          SseEvent(_event.isEmpty ? 'message' : _event, _data.join('\n')),
        );
      }
      _data.clear();
      _event = '';
      return;
    }
    if (line.startsWith(':')) return;
    final colon = line.indexOf(':');
    final field = colon < 0 ? line : line.substring(0, colon);
    var value = colon < 0 ? '' : line.substring(colon + 1);
    if (value.startsWith(' ')) value = value.substring(1);
    switch (field) {
      case 'event':
        _event = value;
      case 'data':
        _data.add(value);
    }
  }

  /// Decodes UTF-8 bytes (a character may be split across chunks) and parses.
  /// Built from plain stream operators rather than an async generator, so a
  /// cancel reaches the request at once even while no bytes are arriving.
  static Stream<SseEvent> parse(Stream<List<int>> bytes) {
    final parser = SseParser();
    return bytes.transform(utf8.decoder).expand(parser.add);
  }
}
