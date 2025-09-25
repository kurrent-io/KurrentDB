// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics;
using Grpc.Core;
using KurrentDB.Testing.TUnit;
using Serilog.Context;
using Serilog.Core;
using Serilog.Core.Enrichers;
using TUnit.Core.Interfaces;
using static Serilog.Core.Constants;

namespace KurrentDB.Testing;

public class ToolkitTestExecutor : ITestExecutor {
    public async ValueTask ExecuteTest(TestContext context, Func<ValueTask> action) {
         context.AddAsyncLocalValues();

         var testUid = context.TestUid();

         var (logger, capturedLogs) = ToolkitTestEnvironment.CaptureTestLogs(testUid, context.TestDetails.ClassType.FullName);

         await using var logging = context.AddLogging(logger);

         ILogEventEnricher[] enrichers = [
             new PropertyEnricher(nameof(TestUid), testUid),
             new PropertyEnricher(SourceContextPropertyName, context.TestDetails.ClassType.FullName)
         ];

         using var _ = LogContext.Push(enrichers);

         try {
             await action();
         }
         catch (RpcException ex) {
             var status = ex.GetRpcStatus()!;

             logger.Error(
                 ex.Status.DebugException,
                 "{TestClass} {TestName} {State} {ErrorMessage}",
                 context.TestDetails.ClassType.Name, context.TestDetails.TestName, TestState.Failed,
                 ex.Status.Detail
             );

             var errorMessage =
                 $"*** gRPC Request Failed ***{Environment.NewLine}"
               + $"Status:  {ex.StatusCode} ({ex.StatusCode.GetHashCode()}){Environment.NewLine}"
               + $"Error:   {ex.Status.Detail}{Environment.NewLine}"
               + $"Details:{Environment.NewLine}{string.Join($"{Environment.NewLine}", status.Details.Select(d => $"  - {d.TypeUrl}"))}{Environment.NewLine}";

             throw new Exception(errorMessage, ex.Status.DebugException).Demystify();
         }
         catch (Exception ex) {
             logger.Error(
                 ex, "{TestClass} {TestName} {State} {ErrorMessage}", context.TestDetails.ClassType.Name,
                 context.TestDetails.TestName, TestState.Failed, ex.Message
             );

             throw ex.Demystify();
         }
         finally {
             await capturedLogs.DisposeAsync();
         }
    }
}
