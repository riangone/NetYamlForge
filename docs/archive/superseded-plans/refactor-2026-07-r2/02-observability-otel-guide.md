# OpenTelemetry 遥测接入指南 (OTEL / OTLP)

本指南介绍如何在本地和生产环境中启用、调试和接入 NetYamlForge 的 OpenTelemetry 遥测（Metrics/Tracing）系统。

## 1. 本地开发调试：使用 Console Exporter

在本地开发时，如果你不想启动复杂的后端收集器，可以直接将 Traces 和 Metrics 打印到控制台。

### 1.1 配置方式
在 `NetYamlForge/appsettings.Development.json` 或 `appsettings.json` 中配置：

```json
{
  "Forge": {
    "Telemetry": {
      "Enabled": true,
      "Exporter": "Console",
      "Metrics": true,
      "Tracing": true
    }
  }
}
```

### 1.2 预期输出
启动应用后，只要发生动态实体查询或批处理执行，控制台就会打印以下格式的 OpenTelemetry 遥测日志：

- **Tracing 样例**：
  ```
  Activity.TraceId:            a4fb3c1290bb4c9e88d752c1ef41ea02
  Activity.SpanId:             cf83bda4957e8412
  Activity.DisplayName:        forge.batch.photo_annotator
  Activity.Kind:               Internal
  Activity.StartTime:          2026-07-06T05:22:15.1234560Z
  Activity.Duration:           00:00:01.4500000
  Activity.Tags:
    project: photo-vocab
    job: photo_annotation_job
    step: photo_annotator
  ```

## 2. 生产环境接入：使用 OTLP Exporter 和 Collector

在生产环境中，应将遥测数据导出到 OTLP 兼容的 APM 后端（如 Jaeger, Prometheus, OpenSearch, Datadog 或 Dynatrace）。

### 2.1 典型拓扑
```
NetYamlForge App (OTLP Push) ---> OpenTelemetry Collector ---> Jaeger (Traces)
                                                          ---> Prometheus (Metrics)
```

### 2.2 导出配置
修改 `appsettings.json` 以使用 OTLP 导出器：

```json
{
  "Forge": {
    "Telemetry": {
      "Enabled": true,
      "Exporter": "Otlp",
      "OtlpEndpoint": "http://otel-collector:4317",
      "SampleRatio": 1.0,
      "Metrics": true,
      "Tracing": true
    }
  }
}
```

### 2.3 OpenTelemetry Collector 配置参考 (`otel-collector-config.yaml`)

```yaml
receivers:
  otlp:
    protocols:
      grpc:
        endpoint: 0.0.0.0:4317
      http:
        endpoint: 0.0.0.0:4318

exporters:
  otlp/jaeger:
    endpoint: jaeger:4317
    tls:
      insecure: true
  prometheus:
    endpoint: 0.0.0.0:8889

processors:
  batch:

service:
  pipelines:
    traces:
      receivers: [otlp]
      processors: [batch]
      exporters: [otlp/jaeger]
    metrics:
      receivers: [otlp]
      processors: [batch]
      exporters: [prometheus]
```
