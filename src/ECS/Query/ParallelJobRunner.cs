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
    private  readonly   object                  jobLock = new object();
    private             int                     pendingWorkerCount;
    private             int                     startedWorkers;
    private             JobTask[]               jobTasks;
    internal readonly   int                     workerCount;
    private             bool                    running;
    
    private  static readonly long               SpinBeforeParkTicks = Stopwatch.Frequency * 50 / 1_000_000;
    private  const      int                     MaxThreadCount      = 256; // a task index must fit a byte
    #endregion
#region debug fields
    private  readonly   string                  name;                   // never null
    private             object                  usedBy;                 // only used for exceptions to provide context
    // Whose task started the current job; walking it catches nesting through other runners that would deadlock.
    private             ParallelJobRunner       enclosing;
    [ThreadStatic]
    private  static     ParallelJobRunner       _tlsCurrent;            // the runner whose task this thread executes
    private  static     int                     _jobRunnerIdSeq;
    #endregion
    
#region general
    public ParallelJobRunner(int threadCount, string name = null) {
        if (threadCount < 1 || threadCount > MaxThreadCount) {
            throw new ArgumentOutOfRangeException(nameof(threadCount), threadCount, $"must be in range [1, {MaxThreadCount}]");
        }
        workerCount         = threadCount - 1;
        running             = true;
        workerStarts        = new ManualResetEventSlim[workerCount];
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
        if (IsUsedByCurrentThread()) {
            throw new InvalidOperationException($"'{name}' cannot be disposed by one of its own tasks");
        }
        ThreadInterruptedException interrupted = null;
        lock (jobLock) {
            running     = false;
            foreach (var start in workerStarts) {
                interrupted = SetUninterrupted(start) ?? interrupted;
            }
        }
        if (interrupted != null) {
            throw interrupted;
        }
    }

    private void StartWorkers()
    {
        while (startedWorkers < workerCount)
        {
            var index   = startedWorkers + 1;
            var thread  = new Thread(RunWorker) {
                Name            = $"{name} - worker {index}",
                IsBackground    = true
            };
            // A worker outlives the caller that starts it, so it must not carry that caller's AsyncLocal state.
            using (ExecutionContext.SuppressFlow()) {
                thread.Start(index);
            }
            startedWorkers = index;
        }
    }
    
    private ThreadInterruptedException AddTaskException (Exception exception)
    {
        ThreadInterruptedException interrupted = null;
        while (true) {
            try {
                lock (taskExceptions) {
                    taskExceptions.Add(exception);
                }
                return interrupted;
            } catch (ThreadInterruptedException e) {
                interrupted = e;
            }
        }
    }
    
    // Set locks while a thread waits: an interrupt there must not leave a job half started or never finished.
    private static ThreadInterruptedException SetUninterrupted (ManualResetEventSlim signal)
    {
        ThreadInterruptedException interrupted = null;
        while (true) {
            try {
                signal.Set();
                return interrupted;
            } catch (ThreadInterruptedException e) {
                interrupted = e;
            }
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
    
    private bool IsUsedByCurrentThread() {
        for (var runner = _tlsCurrent; runner != null; runner = runner.enclosing) {
            if (runner == this) return true;
        }
        return false;
    }
    
    internal void ThrowIfUnusable() {
        if (!running)                   throw RunnerDisposedException();
        if (IsUsedByCurrentThread())    throw AlreadyUsedByException(usedBy);
    }
    #endregion
    
    // ----------------------------------- job on caller thread -----------------------------------
    internal void ExecuteJob(object job, JobTask[] tasks)
    {
        if (IsUsedByCurrentThread()) throw AlreadyUsedByException(usedBy);
        lock (jobLock)
        {
            if (!running)       throw RunnerDisposedException();
            taskExceptions.Clear();
            StartWorkers();
            var caller  = _tlsCurrent;
            enclosing   = caller;
            jobTasks    = tasks;
            usedBy      = job;
            
            ThreadInterruptedException interrupted = null;
            Volatile.Write(ref pendingWorkerCount, workerCount);
            foreach (var start in workerStarts) {
                interrupted = SetUninterrupted(start) ?? interrupted; // all worker threads start running ...
            }
            
            _tlsCurrent = this;
            try {
                tasks[0].ExecuteTask();
            } catch (Exception exception) {
                interrupted = AddTaskException(exception) ?? interrupted;
            } finally {
                _tlsCurrent = caller;
            }
            
            if (workerCount > 0) {
                // Leaving before every worker finished would let the next job's count absorb this job's late decrement.
                while (true) {
                    try {
                        allWorkersFinished.Wait();
                        break;
                    } catch (ThreadInterruptedException exception) {
                        interrupted = exception;
                    }
                }
                allWorkersFinished.Reset();
            }
            
            AssertWorkerStartsTaken();
            
            jobTasks    = null;
            usedBy      = null;
            enclosing   = null;
            if (interrupted != null) {
                throw interrupted;
            }
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
        while (true)
        {
            AwaitStart(start);
            start.Reset();
            if (!running) break;
            
            // --- execute task
            _tlsCurrent = this;
            try {
                jobTasks[index].ExecuteTask();
            } catch (Exception exception) {
                AddTaskException(exception);
            } finally {
                _tlsCurrent = null;
            }
            // ---
            var pending = Interlocked.Decrement(ref pendingWorkerCount);
            AssertPendingWorkerCount(pending);
            if (pending == 0) {
                SetUninterrupted(allWorkersFinished);
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
        while (true) {
            try {
                start.Wait();
                return;
            } catch (ThreadInterruptedException) {
                // A task that interrupts its worker must not kill the worker, which would hang every later job.
            }
        }
    }
}