using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Friflo.Engine.ECS;
using NUnit.Framework;
using Tests.ECS.Index;
using Tests.Utils;

// ReSharper disable AccessToModifiedClosure
// ReSharper disable UnusedVariable
// ReSharper disable RedundantLambdaParameterType
// ReSharper disable UnusedParameter.Local
// ReSharper disable once InconsistentNaming
namespace Tests.ECS.Arch {

public static class Test_QueryJob
{
    [Test]
    public static void Test_QueryJob_Run()
    {
        var store       = new EntityStore(PidType.UsePidAsId);
        var archetype   = store.GetArchetype(ComponentTypes.Get<MyComponent1>());
        for (int n = 0; n < 32; n++) {
            archetype.CreateEntity();
        }
        
        ArchetypeQuery<MyComponent1> query = store.Query<MyComponent1>();
        
        // --- use same interface by ForEach() as in foreach loop
        foreach (var  (component1, entities) in query.Chunks) { }
        query.ForEach((component1, entities) => { });
        
        foreach      ((Chunk<MyComponent1> component1, ChunkEntities entities) in query.Chunks) { }
        query.ForEach((Chunk<MyComponent1> component1, ChunkEntities entities) => { });
        
        // --- execute ForEach() single threaded
        int taskCount = 0;
        var job = query.ForEach((component1, entities) => {
            Mem.IsTrue(entities.Execution == JobExecution.Sequential);
            Mem.AreEqual(0, entities.TaskIndex);
            taskCount++;
        });
        job.Run();
        
        using var runner = new ParallelJobRunner(1);  
        job.JobRunner = runner;
        job.RunParallel();
        
        Assert.AreEqual(2, taskCount);
    }
    
    [Test]
    public static void Test_QueryJob_RunParallel()
    {
        long count       = 10;      // 100_000;
        long entityCount = 10_000;  // 100_000;
        int  threadCount = 2;
        
        var store       = new EntityStore(PidType.UsePidAsId);
        var archetype   = store.GetArchetype(ComponentTypes.Get<MyComponent1>());
        for (int n = 0; n < entityCount; n++) {
            archetype.CreateEntity();
        }
        
        var query = store.Query<MyComponent1>();
        var forEachCount    = 0;
        var lengthSum       = 0L;
        
        var job = query.ForEach((component1, entities) =>
        {
            Mem.IsTrue(entities.Execution == JobExecution.Parallel);
            Interlocked.Increment(ref forEachCount);
            Interlocked.Add(ref lengthSum, entities.Length);
            var componentSpan = component1.Span;
            foreach (ref var c in componentSpan) {
                ++c.a;
            }
        });
        using var runner = new ParallelJobRunner(threadCount);
        job.JobRunner               = runner;
        job.MinParallelChunkLength  = 1000;
        job.RunParallel();  // force one time allocations

        var start   = Mem.GetAllocatedBytes();
        Mem.AssertNoAlloc(start);
        job.RunParallel();
        
        var sw      = new Stopwatch();
        sw.Start();
        for (int n = 2; n < count; n++) {
            job.RunParallel(); // allocate occasionally 24 byte for the entire loop in DEBUG
        }
        var duration = sw.ElapsedMilliseconds;
        
        Console.Write($"JobQuery.RunParallel() - entities: {entityCount}, threads: {threadCount}, count: {count}, duration: {duration}" );
        
        Assert.AreEqual(threadCount * count, forEachCount);
        Assert.AreEqual(entityCount * count, lengthSum);
    }
    
    static readonly bool LogMinParallel = false;
    
    [Test]
    public static void Test_QueryJob_MinParallelChunkLength()
    {
        int threadCount = 4;
        var store       = new EntityStore(PidType.UsePidAsId);
        var archetype   = store.GetArchetype(ComponentTypes.Get<MyComponent1>());
        var query       = store.Query<MyComponent1>();
        
        int minParallel = 0;
        int entityCount = 0;
        var job         = query.ForEach((component1, entities) =>
        {
            if (LogMinParallel) {
                Console.WriteLine($"minParallel: {minParallel}, length: {entities.Length}, entityCount: {entityCount} {entities.Execution}");
            }
            if ((entityCount + threadCount - 1) / threadCount >= minParallel) {
                Mem.IsTrue(entities.Execution == JobExecution.Parallel);
            } else {
                Mem.IsTrue(entities.Execution == JobExecution.Sequential);
            }
        });
        using var runner = new ParallelJobRunner(threadCount);
        job.JobRunner               = runner;

        for (entityCount = 0; entityCount < 4 * 4; entityCount++)
        {
            for (minParallel = 1; minParallel <= 4; minParallel++)
            {
                job.MinParallelChunkLength = minParallel;
                job.RunParallel();
            }
            if (LogMinParallel) Console.WriteLine();
            archetype.CreateEntity().AddComponent<MyComponent1>();
        }
    }

    /// all TPL <see cref="Parallel"/> methods allocate memory. SO they are out.
    [Test]
    public static void Test_QueryJob_TPL()
    {
        // --- Parallel.ForEach()
        var array = new int[100];
        var start = Mem.GetAllocatedBytes();
        Parallel.ForEach(array, value => { _ = value; });
        Mem.AssertAllocations(start);
        
        // --- Parallel.For()
        start = Mem.GetAllocatedBytes();
        Parallel.For(0, 10, i => {});
        Mem.AssertAllocations(start);
        
        // --- Parallel.Invoke()
        var actions = new Action[4];
        actions[0] = () => {};
        actions[1] = () => {};
        actions[2] = () => {};
        actions[3] = () => {};
        start = Mem.GetAllocatedBytes();
        Parallel.Invoke(actions);
        Mem.AssertAllocations(start);
    }
    
    [Test]
    public static void Test_QueryJob_task_exceptions()
    {
        var store       = new EntityStore(PidType.UsePidAsId);
        var archetype   = store.GetArchetype(ComponentTypes.Get<MyComponent1>());
        for (int n = 0; n < 32; n++) {
            archetype.CreateEntity();
        }
        
        var query = store.Query<MyComponent1>();
        
        var job     = query.ForEach((component1, entities) => throw new InvalidOperationException("test exception"));
        job.MinParallelChunkLength = 10;
        
        using var runner  = new ParallelJobRunner(2);
        Assert.AreEqual(2, runner.ThreadCount);
        job.JobRunner = runner;
        Assert.AreSame(runner, job.JobRunner);
        
        var e   = Assert.Throws<AggregateException>(() => {
            job.RunParallel();
        });
        Assert.AreEqual(2, e!.InnerExceptions.Count);
        Assert.AreEqual("QueryJob [MyComponent1] - 2 task exceptions. (test exception) (test exception)", e!.Message);
        
        var e2  = Assert.Throws<InvalidOperationException>(() => {
            job.Run();
        });
        Assert.AreEqual("test exception", e2!.Message);
    }
    
    [Test]
    public static void Test_QueryJob_QueryJob_exceptions()
    {
        var store   = new EntityStore(PidType.UsePidAsId);
        var query   = store.Query<MyComponent1>();
        var job     = query.ForEach((_,_) => {});
        
        var e1 = Assert.Throws<InvalidOperationException>(() => {
            job.RunParallel();
        });
        Assert.AreEqual("QueryJob requires a JobRunner", e1!.Message);
        
        job.MinParallelChunkLength = 10000;
        Assert.AreEqual(10000, job.MinParallelChunkLength);
        
        var e2 = Assert.Throws<ArgumentException>(() => {
            job.MinParallelChunkLength = 0;
        });
        Assert.AreEqual("MinParallelChunkLength must be > 0", e2!.Message);
        
        var e3 = Assert.Throws<ArgumentNullException>(() => {
            job.JobRunner = null;
        });
        Assert.AreEqual("jobRunner", e3!.ParamName);
    }
    
    private static void SetThreadName(string name) {
#if UNITY_5_3_OR_NEWER
        if (Thread.CurrentThread.Name == name) return; // can set thread name only once in Unity
#endif
        Thread.CurrentThread.Name = name;
    }
    
    [Test]
    public static void Test_QueryJob_nested_ForEach()
    {
        SetThreadName("MainThread");
        using var runner    = new ParallelJobRunner(2, "JobRunner");
        var store           = new EntityStore(PidType.UsePidAsId) { JobRunner = runner };
        for (int n = 0; n < 32; n++) {
            store.CreateEntity().AddComponent<MyComponent1>();
        }
        
        var count       = 0;
        var query       = store.Query<MyComponent1>();
        var nestedJob   = query.ForEach((_, entities) => {
            Mem.IsTrue(entities.Execution == JobExecution.Parallel);
            Interlocked.Increment(ref count);
        });
        nestedJob.MinParallelChunkLength = 1;
        var enclosingJob = query.ForEach((_,_) => {
            nestedJob.RunParallel(); // throws '... is already used by ...' exception if using the same job runner
        });
        Assert.AreEqual(1000, enclosingJob.MinParallelChunkLength);
        
        // --- error case
        enclosingJob.MinParallelChunkLength = 1;
        var e = Assert.Throws<AggregateException>(enclosingJob.RunParallel)!;
        
        var exceptions =  e.InnerExceptions;
        Assert.AreEqual(2, exceptions.Count);
        Assert.AreEqual("'JobRunner' is already used by <- QueryJob [MyComponent1]", exceptions[0].Message);
        Assert.AreEqual("'JobRunner' is already used by <- QueryJob [MyComponent1]", exceptions[1].Message);
        
        // --- happy case
        count = 0;
        using var runner2 = new ParallelJobRunner(2, "JobRunner 2");
        nestedJob.JobRunner = runner2;  // assign a separate job runner
        enclosingJob.RunParallel();
        Assert.AreEqual(4, count);
    }
    
    [Test]
    public static void Test_QueryJob_multi_thread_JobRunner()
    {
        var threads = 4; 
        using var runner    = new ParallelJobRunner(threads, "TestRunner");
        var store           = new EntityStore(PidType.UsePidAsId) { JobRunner = runner };
        for (int n = 0; n < 64; n++) {
            var entity  = store.CreateEntity();
            entity.AddComponent<MyComponent1>();
            entity.AddComponent<MyComponent2>();
        }
        var query1  = store.Query<MyComponent1>();
        var query2  = store.Query<MyComponent2>();
        var count1  = 0;
        var count2  = 0;
        var job1    = query1.ForEach((_, entities) => {
            Mem.IsTrue(entities.Execution == JobExecution.Parallel);
            Interlocked.Increment(ref count1);
        }); 
        var job2    = query2.ForEach((_, entities) => {
            Mem.IsTrue(entities.Execution == JobExecution.Parallel);
            Interlocked.Increment(ref count2);
        });
        job1.MinParallelChunkLength = 1;
        job2.MinParallelChunkLength = 1;
        
        var jobs    = new QueryJob [] { job1, job2 };
        Parallel.ForEach(jobs, job =>
        {
            for (int n = 0; n < 1000; n++) {
                job.RunParallel();
            }
        });
        Console.WriteLine($"{count1} {count2}");
        Assert.AreEqual(threads * 1000, count1);
        Assert.AreEqual(threads * 1000, count2);
    }

    [Test]
    public static void Test_QueryJob_RunParallel_two_archetypes()
    {
        var threads = 4;
        using var runner    = new ParallelJobRunner(threads);
        var store           = new EntityStore(PidType.UsePidAsId) { JobRunner = runner };
        var single          = store.GetArchetype(ComponentTypes.Get<MyComponent1>());
        var pair            = store.GetArchetype(ComponentTypes.Get<MyComponent1, MyComponent2>());
        for (int n = 0; n < 64; n++) {
            single.CreateEntity();
            pair.CreateEntity();
        }
        var calls   = 0;
        var job     = store.Query<MyComponent1>().ForEach((component1, entities) => {
            Interlocked.Increment(ref calls);
            foreach (ref var c in component1.Span) {
                ++c.a;
            }
        });
        job.MinParallelChunkLength = 1;
        job.RunParallel();
        job.RunParallel();

        Assert.AreEqual(threads * 2 * 2, calls);
        foreach (var entity in store.Entities) {
            Assert.AreEqual(2, entity.GetComponent<MyComponent1>().a);
        }
    }
    
    [Test]
    public static void Test_QueryJob_RunParallel_concurrent_callers()
    {
        var runs = 1000;
        using var runner    = new ParallelJobRunner(4);
        var store           = new EntityStore(PidType.UsePidAsId) { JobRunner = runner };
        var archetypes      = new [] {
            store.GetArchetype(ComponentTypes.Get<MyComponent1>()),
            store.GetArchetype(ComponentTypes.Get<MyComponent1, MyComponent2>()),
            store.GetArchetype(ComponentTypes.Get<MyComponent1, MyComponent3>()),
            store.GetArchetype(ComponentTypes.Get<MyComponent1, MyComponent4>()),
        };
        foreach (var archetype in archetypes) {
            for (int n = 0; n < 64; n++) {
                archetype.CreateEntity();
            }
        }
        var job = store.Query<MyComponent1>().ForEach((component1, entities) => {
            foreach (ref var c in component1.Span) {
                Interlocked.Increment(ref c.a);
            }
        });
        job.MinParallelChunkLength = 1;
        
        Parallel.Invoke(
            () => { for (int n = 0; n < runs; n++) job.RunParallel(); },
            () => { for (int n = 0; n < runs; n++) job.RunParallel(); });
        
        foreach (var entity in store.Entities) {
            Assert.AreEqual(2 * runs, entity.GetComponent<MyComponent1>().a);
        }
    }
    
    [Test]
    public static void Test_QueryJob_Dispose_from_own_task()
    {
        using var runner    = new ParallelJobRunner(2);
        var store           = new EntityStore(PidType.UsePidAsId) { JobRunner = runner };
        for (int n = 0; n < 32; n++) {
            store.CreateEntity().AddComponent<MyComponent1>();
        }
        var job = store.Query<MyComponent1>().ForEach((_, entities) => {
            if (entities.TaskIndex == 1) {
                runner.Dispose();
            }
        });
        job.MinParallelChunkLength = 1;
        
        var e = Assert.Throws<AggregateException>(job.RunParallel)!;
        Assert.IsInstanceOf<InvalidOperationException>(e.InnerExceptions[0]);
        Assert.IsFalse(runner.IsDisposed);
    }

    [Test]
    public static void Test_QueryJob_RunParallel_own_job_from_task()
    {
        using var runner    = new ParallelJobRunner(2);
        var store           = new EntityStore(PidType.UsePidAsId) { JobRunner = runner };
        for (int n = 0; n < 64; n++) {
            store.CreateEntity().AddComponent<MyComponent1>();
        }
        QueryJob job    = null;
        var refused     = 0;
        job = store.Query<MyComponent1>().ForEach((component1, entities) => {
            try {
                job!.RunParallel();
            } catch (InvalidOperationException) {
                Interlocked.Increment(ref refused);
            }
            foreach (ref var c in component1.Span) {
                ++c.a;
            }
        });
        job.MinParallelChunkLength = 1;
        job.RunParallel();
        
        Assert.AreEqual(2, refused);
        foreach (var entity in store.Entities) {
            Assert.AreEqual(1, entity.GetComponent<MyComponent1>().a);
        }
    }
    
    [Test]
    public static void Test_QueryJob_RunParallel_cycle_through_other_runner()
    {
        using var runner1   = new ParallelJobRunner(2);
        using var runner2   = new ParallelJobRunner(2);
        var store           = new EntityStore(PidType.UsePidAsId);
        for (int n = 0; n < 64; n++) {
            store.CreateEntity(new MyComponent1(), new MyComponent2());
        }
        var refused     = 0;
        var innermost   = store.Query<MyComponent1>().ForEach((_, _) => { });
        innermost.JobRunner = runner1;
        innermost.MinParallelChunkLength = 1;
        var inner       = store.Query<MyComponent2>().ForEach((_, _) => {
            try {
                innermost.RunParallel();
            } catch (InvalidOperationException) {
                Interlocked.Increment(ref refused);
            }
        });
        inner.JobRunner = runner2;
        inner.MinParallelChunkLength = 1;
        var outer       = store.Query<MyComponent1>().ForEach((_, entities) => {
            if (entities.TaskIndex == 1) {
                inner.RunParallel();
            }
        });
        outer.JobRunner = runner1;
        outer.MinParallelChunkLength = 1;
        
        outer.RunParallel();
        
        Assert.AreEqual(2, refused);
    }
    
    [Test]
    public static void Test_QueryJob_RunParallel_nested_runners_sharing_a_name()
    {
        using var runner1   = new ParallelJobRunner(2, "Same");
        using var runner2   = new ParallelJobRunner(2, "Same");
        var store           = new EntityStore(PidType.UsePidAsId);
        for (int n = 0; n < 64; n++) {
            store.CreateEntity(new MyComponent1(), new MyComponent2());
        }
        var inner = store.Query<MyComponent2>().ForEach((component2, _) => {
            foreach (ref var c in component2.Span) {
                Interlocked.Increment(ref c.b);
            }
        });
        inner.JobRunner = runner2;
        inner.MinParallelChunkLength = 1;
        var outer = store.Query<MyComponent1>().ForEach((_, entities) => {
            if (entities.TaskIndex == 1) {
                inner.RunParallel();
            }
        });
        outer.JobRunner = runner1;
        outer.MinParallelChunkLength = 1;
        
        outer.RunParallel();
        
        foreach (var entity in store.Entities) {
            Assert.AreEqual(1, entity.GetComponent<MyComponent2>().b);
        }
    }
    
    [Test]
    public static void Test_QueryJob_RunParallel_value_condition_sections()
    {
        using var runner    = new ParallelJobRunner(2);
        var store           = new EntityStore(PidType.UsePidAsId) { JobRunner = runner };
        for (int n = 0; n < 8; n++) {
            store.CreateEntity(new IndexedInt { value = n });
        }
        var query       = store.Query<IndexedInt>().HasValue<IndexedInt, int>(5);
        var sequential  = new List<int>();
        var parallel    = new List<int>();
        query.ForEach((_, entities) => sequential.AddRange(entities.Ids.ToArray())).Run();
        var job = query.ForEach((_, entities) => {
            lock (parallel) {
                parallel.AddRange(entities.Ids.ToArray());
            }
        });
        job.MinParallelChunkLength = 1;
        job.RunParallel();
        
        Assert.AreEqual(new [] { 6 }, sequential);
        Assert.AreEqual(sequential, parallel);
    }
    
    [Test]
    public static void Test_QueryJob_RunParallel_refuses_structural_change_in_task()
    {
        using var runner    = new ParallelJobRunner(2);
        var store           = new EntityStore(PidType.UsePidAsId) { JobRunner = runner };
        for (int n = 0; n < 64; n++) {
            store.CreateEntity().AddComponent<MyComponent1>();
        }
        var job = store.Query<MyComponent1>().ForEach((_, entities) => {
            if (entities.TaskIndex == 1) {
                entities.EntityAt(0).AddTag<TestTag>();
            }
        });
        job.MinParallelChunkLength = 1;
        
        var e = Assert.Throws<AggregateException>(job.RunParallel)!;
        Assert.IsInstanceOf<StructuralChangeException>(e.InnerExceptions[0]);
    }
    
    [Test]
    public static void Test_QueryJob_RunParallel_after_worker_interrupt()
    {
        using var runner    = new ParallelJobRunner(2);
        var store           = new EntityStore(PidType.UsePidAsId) { JobRunner = runner };
        for (int n = 0; n < 64; n++) {
            store.CreateEntity().AddComponent<MyComponent1>();
        }
        var interrupt   = true;
        var job         = store.Query<MyComponent1>().ForEach((component1, entities) => {
            if (entities.TaskIndex == 1 && interrupt) {
                interrupt = false;
                Thread.CurrentThread.Interrupt();
            }
            foreach (ref var c in component1.Span) {
                ++c.a;
            }
        });
        job.MinParallelChunkLength = 1;
        
        job.RunParallel();
        Thread.Sleep(50);
        for (int n = 0; n < 10; n++) {
            job.RunParallel();
            Thread.Sleep(1);
        }
        
        foreach (var entity in store.Entities) {
            Assert.AreEqual(11, entity.GetComponent<MyComponent1>().a);
        }
    }
    
    private static readonly AsyncLocal<int> CallerContext = new AsyncLocal<int>();
    
    [Test]
    public static void Test_QueryJob_RunParallel_workers_do_not_inherit_caller_context()
    {
        using var runner    = new ParallelJobRunner(2);
        var store           = new EntityStore(PidType.UsePidAsId) { JobRunner = runner };
        for (int n = 0; n < 64; n++) {
            store.CreateEntity().AddComponent<MyComponent1>();
        }
        var seenByWorker = -1;
        var job = store.Query<MyComponent1>().ForEach((_, entities) => {
            if (entities.TaskIndex == 1) {
                seenByWorker = CallerContext.Value;
            }
        });
        job.MinParallelChunkLength = 1;
        
        CallerContext.Value = 42;
        job.RunParallel();
        CallerContext.Value = 0;
        
        Assert.AreEqual(0, seenByWorker);
    }
    
    [Test]
    public static void Test_QueryJob_EntityStore_JobRunner()
    {
        using var jobRunner = new ParallelJobRunner(2, "MyRunner");
        Assert.AreEqual("MyRunner - threads: 2", jobRunner.ToString());
        
        var store       = new EntityStore(PidType.UsePidAsId) {
            JobRunner = jobRunner   // attach JobRunner to EntityStore
        };
        for (int n = 0; n < 32; n++) {
            store.CreateEntity().AddComponent<MyComponent1>();
        }
        var query       = store.Query<MyComponent1>();
        int count       = 0;
        SetThreadName("MainThread");

        var job         = query.ForEach((_, entities) => {
            count++;
            Mem.IsTrue(entities.Execution == JobExecution.Parallel);
            var threadName = Thread.CurrentThread.Name;
            switch (entities.TaskIndex) {
                case 0:     Assert.AreEqual("MainThread",           threadName);  break;
                case 1:     Assert.AreEqual("MyRunner - worker 1",  threadName);  break;
                default:    throw new InvalidOperationException("unexpected");
            }
        });
        Assert.AreEqual(1000, job.MinParallelChunkLength);
        job.MinParallelChunkLength = 1;
        job.RunParallel();  // uses JobRunner from EntityStore
        
        Assert.AreEqual(2, count);
    }
    
    [Test]
    public static void Test_QueryJob_QueryJobDispose()
    {
        var store   = new EntityStore(PidType.UsePidAsId);
        var archetype   = store.GetArchetype(ComponentTypes.Get<MyComponent1>());
        for (int n = 0; n < 64; n++) {
            archetype.CreateEntity();
        }
        var runner  = new ParallelJobRunner(4);
        var query   = store.Query<MyComponent1>();
        var job     = query.ForEach((_,_) => {});
        job.MinParallelChunkLength  = 10;
        job.JobRunner               = runner;
        
        job.RunParallel();
        
        runner.Dispose();
        var e = Assert.Throws<ObjectDisposedException>(() => {
            job.RunParallel();    
        });
        // no check of e.Message. uses different line ending \r\n \n on different platforms
        
        var e2 = Assert.Throws<ArgumentException>(() => {
            job.JobRunner = runner;
        });
        Assert.AreEqual("ParallelJobRunner is disposed", e2!.Message);
    }
    
    [Test]
    public static void Test_QueryJob_ParallelComponentMultiple()
    {
        var store = new EntityStore(PidType.RandomPids);
        var multiple = store.Query<ByteComponent>() .ForEach((_, _) => {}).ParallelComponentMultiple;
        Assert.AreEqual(64, multiple);
        
        multiple = store.Query<ShortComponent>()    .ForEach((_, _) => {}).ParallelComponentMultiple;
        Assert.AreEqual(32, multiple);
        
        multiple = store.Query<LongComponent>()     .ForEach((_, _) => {}).ParallelComponentMultiple;
        Assert.AreEqual(8, multiple);
        
        multiple = store.Query<Position>()          .ForEach((_, _) => {}).ParallelComponentMultiple;
        Assert.AreEqual(16, multiple);
        
        multiple = store.Query<Rotation>()          .ForEach((_, _) => {}).ParallelComponentMultiple;
        Assert.AreEqual(4, multiple);
        
        multiple = store.Query<Transform>()         .ForEach((_, _) => {}).ParallelComponentMultiple;
        Assert.AreEqual(1, multiple);
        
        multiple = store.Query<Component16>()       .ForEach((_, _) => {}).ParallelComponentMultiple;
        Assert.AreEqual(4, multiple);
        
        multiple = store.Query<Component32>()       .ForEach((_, _) => {}).ParallelComponentMultiple;
        Assert.AreEqual(2, multiple);
        
        multiple = store.Query<Component20>()       .ForEach((_, _) => {}).ParallelComponentMultiple;
        Assert.AreEqual(16, multiple);
    }
}

}