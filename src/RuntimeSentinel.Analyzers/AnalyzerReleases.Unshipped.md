### New Rules
Rule ID | Category | Severity | Notes
--------|----------|----------|------
RS1001 | Concurrency | Warning | Avoid Thread.Sleep in application code
RS1002 | Concurrency | Warning | Detect Task.WhenAll fan-out without explicit bound
RS1003 | Concurrency | Warning | Detect Parallel.ForEach without explicit MaxDegreeOfParallelism
RS1004 | Async | Warning | Detect blocking Task.Wait() and Task.Result usage
RS1005 | Memory | Warning | Detect potentially heavy ToList materialization without explicit limit
RS1006 | Communication | Warning | Detect HttpClient instantiation in method scope (possible per-request usage)
RS1007 | Communication | Warning | Detect HttpClient calls without explicit timeout policy
RS1008 | Communication | Warning | Detect unbounded retry loops in HTTP communication paths
RS1009 | Communication | Warning | Detect retry loops in HTTP paths using fixed delay without exponential backoff
RS1010 | Communication | Warning | Detect retry loops with exponential backoff but without jitter
