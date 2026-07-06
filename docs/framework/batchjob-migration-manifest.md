# BatchJob 迁移清单 (BatchJob Migration Manifest)

本清单记录了从 `NetYamlForge` 核心服务层中剥离业务专用 Executor 的详细规划，是执行 P1/P2 阶段迁移的基础依据。

## 迁移 Executor 列表

| 序号 | Executor 类名 | 源文件路径 | 行数 | 现有核心依赖清单 | 目标项目 (projects/) | YAML中 ExecutorType 字符串 | 是否共享 |
|:---:|---|---|:---:|---|---|---|:---:|
| 1 | `PhotoAnnotatorExecutor` | `Services/BatchJob/PhotoAnnotatorExecutor.cs` | 764 | `IWebHostEnvironment`<br>`IConfiguration`<br>`IEmbeddingService`<br>`ILogger` | `photo-vocab` | `photo_annotator` | 否 |
| 2 | `AiDealerEngineExecutor` | `Services/BatchJob/AiDealerEngineExecutor.cs` | 606 | `IAntigravityCliService`<br>`ILogger` | `auto-dealer-demo` | `ai_dealer_engine` | 否 |
| 3 | `InvoiceEmailProcessorExecutor` | `Services/BatchJob/InvoiceEmailProcessorExecutor.cs` | 581 | `IDocumentPdfService`<br>`IAntigravityCliService`<br>`ILogger` | `biz-docs` | `invoice_email_processor` | 否 |
| 4 | `BizCardParserExecutor` | `Services/BatchJob/BizCardParserExecutor.cs` | 577 | `IWebHostEnvironment`<br>`IConfiguration`<br>`IEmbeddingService`<br>`ILogger` | `biz-card` | `biz_card_parser` | 否 |
| 5 | `AutomatedBlogGeneratorExecutor` | `Services/BatchJob/AutomatedBlogGeneratorExecutor.cs` | 492 | `IAntigravityCliService`<br>`ILogger` | `blog` | `automated_blog_generator` | 否 |
| 6 | `ChinaStockBriefingExecutor` | `Services/BatchJob/ChinaStockBriefingExecutor.cs` | 182 | `IChinaStockService`<br>`ILogger` | `todo-app` | `china_stock_briefing` | 否 |

## 依赖关系与接口隔离说明

1. **`IBatchStepHandler`**: 核心与外部 Executor 交互的唯一桥梁。所有迁移的 Executor 将实现 `IBatchStepHandler` 接口。
2. **`IChinaStockService`**: 股票相关服务。核心工程仍持有此服务定义，但具体任务处理逻辑被迁移。
3. **`IDocumentPdfService`**: PDF 文档处理服务，核心通用服务。
4. **`IEmbeddingService`**: 向量化通用接口。
5. **`IAntigravityCliService`**: CLI 执行接口（后续将在 P3 中被 `ICliChainService` 统一拦截并消除直接引用，以增加 Fallback）。
