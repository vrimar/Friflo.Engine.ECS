using System;
using Friflo.Json.Fliox;
using Friflo.Engine.ECS;
using Friflo.Engine.ECS.Serialize;
using NUnit.Framework;
using Tests.ECS.Relations;
using static NUnit.Framework.Assert;

// ReSharper disable InconsistentNaming
namespace Tests.ECS {

public static class Test_Regressions
{
    [Test]
    public static void ChildEntities_HeldAcrossDelete_DoesNotReadRecycledBlock()
    {
        var store  = new EntityStore();
        var parent = store.CreateEntity();
        for (int n = 0; n < 3; n++) {
            parent.AddChild(store.CreateEntity());
        }
        var children = parent.ChildEntities;
        AreEqual(3, children.Count);
        parent.DeleteEntity();

        var other = store.CreateEntity();
        for (int n = 0; n < 3; n++) {
            other.AddChild(store.CreateEntity());
        }
        Throws<NullReferenceException>(() => { var _ = children.Count; });
    }
}

}
