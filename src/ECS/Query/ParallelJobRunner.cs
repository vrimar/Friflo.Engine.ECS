// Copyright (c) Ullrich Praetz - https://github.com/friflo. All rights reserved.
// See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

// ReSharper disable InlineTemporaryVariable
// ReSharper disable once CheckNamespace
namespace Friflo.Engine.ECS;

internal abstract class JobTask {
    internal abstract void ExecuteTask();
}

/// <summary>
/// Specify the way how <see cref="ChunkEntities"/> are provided by either a <c>foreach</c> loop,
/// <see cref="QueryJob.Run"/> or <see cref="QueryJob.RunParallel"/>.
/// </summary>
public enum JobExecution : byte
{
    /// <summary><see cref="ChunkEntities"/> are provided by a <c>foreach</c> loop or <see cref="QueryJob.Run"/>.</summary>
    Sequential  = 0,
    /// <summary><see cref="ChunkEntities"/> are provided by a <see cref="QueryJob.RunParallel"/>.</summary>
    Parallel    = 1
}

/// <summary>
/// Required for parallel - multi threaded - query job execution using <see cref="QueryJob.RunParallel"/>.<br/>
/// See <a href="https://friflo.gitbook.io/friflo.engine.ecs/documentation/query-optimization#parallel-query-job">Example.</a>
/// </summary>
/// <remarks>
/// <see cref="ParallelJobRunner"/> is thread safe.<br/>
/// The intention is to use the same instance for all jobs. E.g. the JobRunner assigned to the <see cref="EntityStore"/>.<br/>
/// The job runner divide a job in multiple tasks each executed on its own core.<br/>
/// This ensures high cache hit rate of code and data structures.<br/>
/// <br/>
/// <i>Constraint</i><br/>
/// When executing nested jobs - running a job within another job - the nested job requires its own runner.<br/> 
/// <br/>
/// Performance related implementation goals:<br/>
/// - Minimize calls to synchronization primitives.<br/>
/// - Use cheap synchronization primitives such as:
///   <see cref="ManualResetEventSlim"/>, <see cref="Interlocked"/> and <see cref="Volatile"/>.<br/>
/// - Minimize thread context switches: a worker that finished its task spins briefly for the next job
///   before it parks, so jobs run back to back reach a running worker.<br/>
/// - Never busy-wait for longer than that: an idle worker is parked and costs no CPU.<br/>
/// Note: To analyze the amount of thread context switches use: Process Explorer > Column > CSwitch Delta.
/// </remarks>
public sealed class ParallelJobRunner : IDisposable
{
#region public properties
    public              bool                    IsDisposed  => !running;
    public              int                     ThreadCount => workerCount + 1;
    public override     string                  ToString()  => $"{name} - threads: {ThreadCount}";
    #endregion
    
#region fields
    // One per worker: with a shared one, a fast worker could take a slow worker's start and run its task twice.
    private  readonly   ManualResetEventSlim[]  workerStarts;
    private  readonly   ManualResetEventSlim    allWorkersFinished;
    private  readonly   List<Exception>         taskExceptions;
    private             int                     pendingWorkerCount;
    private             bool                    workersStarted;
    private             JobTask[]               jobTasks;
    internal readonly   int                     workerCount;
    private             bool                    running;
    
    private  static readonly long               SpinBeforeParkTicks = Stopwatch.Frequency * 50 / 1_000_000;
    #endregion
#region debug fields
    private  readonly   string                  name;                   // never null. used also as monitor 
    private             object                  usedBy;                 // only used for exceptions to provide context
    [ThreadStatic]
    // Used only for throwing an exception if using same job runner twice.
    // Not checking for this case result in a deadlock which is much harder to find.
    private  static     Stack<ParallelJobRunner>_tlsUsedRunnerStack;    // single instance per thread
    private  static     int                     _jobRunnerIdSeq;
    #endregion
    
#region general
    public ParallelJobRunner(int threadCount, string name = null) {
        workerCount         = threadCount - 1;
        running             = true;
        workerStarts        = new ManualResetEventSlim[Math.Max(workerCount, 0)];
        for (int n = 0; n < workerStarts.Length; n++) {
            workerStarts[n] = new ManualResetEventSlim(false, 0);
        }
        allWorkersFinished  = new ManualResetEventSlim(false);
        taskExceptions      = new List<Exception>();
        if (name != null) {
            this.name = name;
        } else {
            this.name = "JobRunner " + ++_jobRunnerIdSeq;
        }
    }

    public void Dispose() {
        var stack = _tlsUsedRunnerStack;
        if (stack != null && stack.Contains(this)) {
            throw new InvalidOperationException($"'{name}' cannot be disposed by one of its own tasks");
        }
        lock (name) {
            running     = false;
            foreach (var start in workerStarts) {
                start.Set();
            }
        }
    }

    private void StartWorkers()
    {
        workersStarted = true;
        for (int index = 1; index <= workerCount; index++)
        {
            var thread = new Thread(RunWorker) {
                Name            = $"{name} - worker {index}",
                IsBackground    = true
            };
            thread.Start(index);
        }
    }
    
    private void AddTaskException (Exception exception)
    {
        lock (taskExceptions) {
            taskExceptions.Add(exception);
        }
    }
    
    private static ObjectDisposedException RunnerDisposedException ()
    {
        return new ObjectDisposedException(nameof(ParallelJobRunner));
    }
    
    private InvalidOperationException AlreadyUsedByException (object job)
    {
        return new InvalidOperationException($"'{name}' is already used by <- {job}");
    }
    
    private AggregateException JobException (object job)
    {
        return new AggregateException($"{job} - {taskExceptions.Count} task exceptions.", taskExceptions);
    }
    
    [Conditional("DEBUG")] [ExcludeFromCodeCoverage]
    private static void AssertPendingWorkerCount(int count) {
        if (count < 0) throw new InvalidOperationException($"unexpected pending worker count: {count}");
    }
    
    [Conditional("DEBUG")] [ExcludeFromCodeCoverage]
    private void AssertWorkerStartsTaken() {
        foreach (var start in workerStarts) {
            if (start.IsSet) throw new InvalidOperationException("a worker start is still set");
        }
    }
    #endregion
    
    // ----------------------------------- job on caller thread -----------------------------------
    internal void ExecuteJob(object job, JobTask[] tasks)
    {
        var stack = _tlsUsedRunnerStack ??= new Stack<ParallelJobRunner>();
        // In a common application without nested jobs: stack.Count == 0
        foreach (var runner in stack) {
            if (runner == this) throw AlreadyUsedByException(usedBy);    
        }
        lock(name)
        {
            if (!running)       throw RunnerDisposedException();
            taskExceptions.Clear();
            if (!workersStarted) {
                StartWorkers();
            }
            jobTasks = tasks;
            usedBy   = job;
            
            Volatile.Write(ref pendingWorkerCount, workerCount);
            foreach (var start in workerStarts) {
                start.Set(); // all worker threads start running ...
            }
            
            stack.Push(this);
            try {
                tasks[0].ExecuteTask();
            } catch (Exception exception) {
                AddTaskException(exception);
            }
            stack.Pop();

            allWorkersFinished.Wait();

            allWorkersFinished.Reset();
            
            AssertWorkerStartsTaken();
            
            jobTasks = null;
            usedBy   = null;
            if (taskExceptions.Count > 0) {
                throw JobException(job);
            }
        }
    }
    
    // ------------------------------------ worker thread loop ------------------------------------
    private void RunWorker(object workerIndex)
    {
        var index   = (int)workerIndex;
        var start   = workerStarts[index - 1];
        var stack   = _tlsUsedRunnerStack = new Stack<ParallelJobRunner>();
        while (true)
        {
            AwaitStart(start);
            start.Reset();
            if (!running) break;
            
            // --- execute task
            stack.Push(this);
            try {
                jobTasks[index].ExecuteTask();
            } catch (Exception exception) {
                AddTaskException(exception);
            }
            stack.Pop();
            // ---
            var pending = Interlocked.Decrement(ref pendingWorkerCount);
            AssertPendingWorkerCount(pending);
            if (pending == 0) {
                allWorkersFinished.Set();
            }
        }
    }
    
    private static void AwaitStart(ManualResetEventSlim start)
    {
        var spinner     = new SpinWait();
        var deadline    = Stopwatch.GetTimestamp() + SpinBeforeParkTicks;
        while (!start.IsSet && Stopwatch.GetTimestamp() < deadline) {
            // SpinOnce past its yield point may Sleep(1), far longer than the whole window.
            if (spinner.NextSpinWillYield) {
                Thread.Yield();
            } else {
                spinner.SpinOnce();
            }
        }
        start.Wait();
    }
}
