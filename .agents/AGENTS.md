# NetYamlForge Agent Rules

## 1. 慢命令与后台任务运行规则
在执行 `dotnet build` 或其他可能导致长时间阻塞的命令时，不要进行同步等待。
调用 `run_command` 工具时，应合理设置 `WaitMsBeforeAsync` 参数（例如 `500` 毫秒），启动后立使其转为后台异步任务并结束当前 Turn。当后台任务完成后，系统会自动通过 reactive wakeup 唤醒 Agent，再处理相应的结果。

## 2. 避免无过滤的广域搜索
在使用 `grep_search` 等工具进行搜索时，应当尽量精确地指定 `Includes` 过滤路径（例如限定在 `projects/diary-companion` 下，或者指定特定文件类型），以减少 I/O 带来的额外网络与时间损耗，防止大范围广域搜索引起平台响应超时。
