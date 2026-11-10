import 'dart:async';

import 'package:dio/dio.dart';

import '../../config/app_config.dart';
import '../../utils/api_error.dart';
import 'event_stream_transport.dart';

EventStreamTransport createTransport() => DioEventStreamTransport();

/// Streams a response through Dio (`ResponseType.stream`). Used everywhere
/// except web.
class DioEventStreamTransport implements EventStreamTransport {
  /// A local model can think for a while between sentences.
  static const Duration idleTimeout = Duration(minutes: 3);

  final Dio _dio;

  DioEventStreamTransport([Dio? dio])
    : _dio =
          dio ??
          Dio(
            BaseOptions(
              connectTimeout: AppConfig.requestTimeout,
              receiveTimeout: idleTimeout,
            ),
          );

  @override
  Future<StreamedHttpResponse> post(
    Uri url, {
    required Map<String, String> headers,
    required String body,
  }) async {
    final cancel = CancelToken();
    final Response<ResponseBody> response;
    try {
      response = await _dio.postUri<ResponseBody>(
        url,
        data: body,
        cancelToken: cancel,
        options: Options(
          responseType: ResponseType.stream,
          headers: headers,
          validateStatus: (_) => true,
        ),
      );
    } on DioException catch (e) {
      throw ApiException.fromDio(e);
    }
    final source = response.data?.stream ?? const Stream<List<int>>.empty();
    StreamSubscription<List<int>>? subscription;
    late final StreamController<List<int>> controller;
    controller = StreamController<List<int>>(
      onListen: () {
        subscription = source.listen(
          controller.add,
          onError: (Object _) {
            controller.addError(const ApiException.network());
            controller.close();
          },
          onDone: controller.close,
          cancelOnError: true,
        );
      },
      onPause: () => subscription?.pause(),
      onResume: () => subscription?.resume(),
      onCancel: () async {
        cancel.cancel();
        await subscription?.cancel();
      },
    );
    return StreamedHttpResponse(
      statusCode: response.statusCode ?? 0,
      retryAfter: response.headers.value('retry-after'),
      body: controller.stream,
    );
  }
}
