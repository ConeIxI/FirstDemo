# Behavior Tree Framework Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a reusable ScriptableObject-based behavior tree framework with `Success` / `Failure` / `Running` support and no enemy-specific logic.

**Architecture:** Behavior tree assets store configuration only. `BehaviorTreeRunner` builds runtime node instances per owner so shared assets never share Running state. Nodes read execution data from `BehaviorTreeContext` and exchange simple facts through `BehaviorTreeBlackboard`.

**Tech Stack:** Unity 2022.3.61f1c1, C# 9.0, NUnit EditMode tests, AIBridge CLI.

---

## File Structure

- Create: `Assets/Framework/Core/BehaviorTree/BehaviorTreeStatus.cs`
  - Defines the three node statuses.
- Create: `Assets/Framework/Core/BehaviorTree/BehaviorTreeBlackboard.cs`
  - Stores simple typed values shared by nodes during one runner's execution.
- Create: `Assets/Framework/Core/BehaviorTree/BehaviorTreeContext.cs`
  - Holds owner, transform, delta time, and blackboard for each tick.
- Create: `Assets/Framework/Core/BehaviorTree/BehaviorTreeAsset.cs`
  - ScriptableObject entry point with a root node reference.
- Create: `Assets/Framework/Core/BehaviorTree/Assets/BehaviorTreeNodeAsset.cs`
  - Base ScriptableObject for all node assets.
- Create: `Assets/Framework/Core/BehaviorTree/Runtime/BehaviorTreeNode.cs`
  - Base runtime node with per-runner state.
- Create: `Assets/Framework/Core/BehaviorTree/BehaviorTreeRunner.cs`
  - Builds runtime nodes and drives tree ticks.
- Create: `Assets/Framework/Core/BehaviorTree/Assets/ConditionNodeAsset.cs`
  - Base class for stateless condition assets.
- Create: `Assets/Framework/Core/BehaviorTree/Assets/ActionNodeAsset.cs`
  - Base class for action assets.
- Create: `Assets/Framework/Core/BehaviorTree/Assets/CompositeNodeAsset.cs`
  - Base class for multi-child node assets.
- Create: `Assets/Framework/Core/BehaviorTree/Nodes/SelectorNodeAsset.cs`
  - Selector node implementation.
- Create: `Assets/Framework/Core/BehaviorTree/Nodes/SequenceNodeAsset.cs`
  - Sequence node implementation.
- Create: `Assets/Framework/Core/BehaviorTree/Assets/DecoratorNodeAsset.cs`
  - Base class for single-child node assets.
- Create: `Assets/Framework/Core/BehaviorTree/Nodes/InverterNodeAsset.cs`
  - Inverter decorator.
- Create: `Assets/Framework/Core/BehaviorTree/Nodes/AlwaysSuccessNodeAsset.cs`
  - Decorator that converts terminal status to success.
- Create: `Assets/Framework/Core/BehaviorTree/Nodes/AlwaysFailureNodeAsset.cs`
  - Decorator that converts terminal status to failure.
- Create: `Assets/Game/Editor/BehaviorTreeBlackboardEditModeTests.cs`
- Create: `Assets/Game/Editor/BehaviorTreeRunnerEditModeTests.cs`
- Create: `Assets/Game/Editor/BehaviorTreeLeafNodeEditModeTests.cs`
- Create: `Assets/Game/Editor/BehaviorTreeCompositeNodeEditModeTests.cs`
- Create: `Assets/Game/Editor/BehaviorTreeDecoratorNodeEditModeTests.cs`

Use namespace `GameMain2.Framework.Core.BehaviorTree` for framework code, matching the existing `GameMain2.Framework.Core.FSM` namespace style.

---

### Task 1: Core Status, Context, And Blackboard

**Files:**
- Create: `Assets/Game/Editor/BehaviorTreeBlackboardEditModeTests.cs`
- Create: `Assets/Framework/Core/BehaviorTree/BehaviorTreeStatus.cs`
- Create: `Assets/Framework/Core/BehaviorTree/BehaviorTreeBlackboard.cs`
- Create: `Assets/Framework/Core/BehaviorTree/BehaviorTreeContext.cs`

- [ ] **Step 1: Write the failing EditMode tests**

Create `Assets/Game/Editor/BehaviorTreeBlackboardEditModeTests.cs`:

```csharp
using GameMain2.Framework.Core.BehaviorTree;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    public sealed class BehaviorTreeBlackboardEditModeTests
    {
        /// <summary>验证黑板可以写入、读取、覆盖和清除常用值。</summary>
        [Test]
        public void Blackboard_CanWriteReadOverrideAndClearValues()
        {
            BehaviorTreeBlackboard blackboard = new BehaviorTreeBlackboard();
            GameObject targetObject = new GameObject("Target");
            try
            {
                blackboard.SetBool("visible", true);
                blackboard.SetInt("count", 3);
                blackboard.SetFloat("distance", 1.5f);
                blackboard.SetVector3("position", new Vector3(1f, 2f, 3f));
                blackboard.SetObject("target", targetObject.transform);

                Assert.IsTrue(blackboard.TryGetBool("visible", out bool visible));
                Assert.IsTrue(visible);
                Assert.IsTrue(blackboard.TryGetInt("count", out int count));
                Assert.AreEqual(3, count);
                Assert.IsTrue(blackboard.TryGetFloat("distance", out float distance));
                Assert.AreEqual(1.5f, distance);
                Assert.IsTrue(blackboard.TryGetVector3("position", out Vector3 position));
                Assert.AreEqual(new Vector3(1f, 2f, 3f), position);
                Assert.IsTrue(blackboard.TryGetObject("target", out Transform target));
                Assert.AreSame(targetObject.transform, target);

                blackboard.SetInt("count", 7);
                Assert.IsTrue(blackboard.TryGetInt("count", out int overwrittenCount));
                Assert.AreEqual(7, overwrittenCount);

                Assert.IsTrue(blackboard.Remove("count"));
                Assert.IsFalse(blackboard.TryGetInt("count", out _));

                blackboard.Clear();
                Assert.IsFalse(blackboard.TryGetBool("visible", out _));
                Assert.IsFalse(blackboard.TryGetObject<Transform>("target", out _));
            }
            finally
            {
                Object.DestroyImmediate(targetObject);
            }
        }

        /// <summary>验证上下文保存行为树执行所需的基础运行时信息。</summary>
        [Test]
        public void Context_UsesOwnerTransformAndProvidedBlackboard()
        {
            GameObject owner = new GameObject("Owner");
            try
            {
                BehaviorTreeBlackboard blackboard = new BehaviorTreeBlackboard();
                BehaviorTreeContext context = new BehaviorTreeContext(owner, blackboard);
                context.DeltaTime = 0.25f;

                Assert.AreSame(owner, context.Owner);
                Assert.AreSame(owner.transform, context.Transform);
                Assert.AreSame(blackboard, context.Blackboard);
                Assert.AreEqual(0.25f, context.DeltaTime);
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }
    }
}
```

- [ ] **Step 2: Run the tests and verify they fail**

Run:

```bash
$CLI test run --mode EditMode --group-name Game.Tests.EditMode.BehaviorTreeBlackboardEditModeTests --timeout 120000
```

Expected: fails because `GameMain2.Framework.Core.BehaviorTree` types do not exist yet.

- [ ] **Step 3: Implement the minimal core files**

Create `Assets/Framework/Core/BehaviorTree/BehaviorTreeStatus.cs`:

```csharp
namespace GameMain2.Framework.Core.BehaviorTree
{
    public enum BehaviorTreeStatus
    {
        Success = 0,
        Failure = 1,
        Running = 2
    }
}
```

Create `Assets/Framework/Core/BehaviorTree/BehaviorTreeBlackboard.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace GameMain2.Framework.Core.BehaviorTree
{
    public sealed class BehaviorTreeBlackboard
    {
        private readonly Dictionary<string, object> values = new Dictionary<string, object>();

        /// <summary>写入对象值，常用于 Transform、组件或其它引用类型。</summary>
        public void SetObject(string key, object value)
        {
            values[key] = value;
        }

        /// <summary>读取指定类型的对象值，类型不匹配时返回 false。</summary>
        public bool TryGetObject<T>(string key, out T value) where T : class
        {
            if (values.TryGetValue(key, out object rawValue) && rawValue is T typedValue)
            {
                value = typedValue;
                return true;
            }

            value = null;
            return false;
        }

        /// <summary>写入布尔值。</summary>
        public void SetBool(string key, bool value)
        {
            values[key] = value;
        }

        /// <summary>读取布尔值。</summary>
        public bool TryGetBool(string key, out bool value)
        {
            if (values.TryGetValue(key, out object rawValue) && rawValue is bool typedValue)
            {
                value = typedValue;
                return true;
            }

            value = false;
            return false;
        }

        /// <summary>写入整数值。</summary>
        public void SetInt(string key, int value)
        {
            values[key] = value;
        }

        /// <summary>读取整数值。</summary>
        public bool TryGetInt(string key, out int value)
        {
            if (values.TryGetValue(key, out object rawValue) && rawValue is int typedValue)
            {
                value = typedValue;
                return true;
            }

            value = 0;
            return false;
        }

        /// <summary>写入浮点值。</summary>
        public void SetFloat(string key, float value)
        {
            values[key] = value;
        }

        /// <summary>读取浮点值。</summary>
        public bool TryGetFloat(string key, out float value)
        {
            if (values.TryGetValue(key, out object rawValue) && rawValue is float typedValue)
            {
                value = typedValue;
                return true;
            }

            value = 0f;
            return false;
        }

        /// <summary>写入三维向量值。</summary>
        public void SetVector3(string key, Vector3 value)
        {
            values[key] = value;
        }

        /// <summary>读取三维向量值。</summary>
        public bool TryGetVector3(string key, out Vector3 value)
        {
            if (values.TryGetValue(key, out object rawValue) && rawValue is Vector3 typedValue)
            {
                value = typedValue;
                return true;
            }

            value = Vector3.zero;
            return false;
        }

        /// <summary>移除指定键值。</summary>
        public bool Remove(string key)
        {
            return values.Remove(key);
        }

        /// <summary>清空所有黑板数据。</summary>
        public void Clear()
        {
            values.Clear();
        }
    }
}
```

Create `Assets/Framework/Core/BehaviorTree/BehaviorTreeContext.cs`:

```csharp
using UnityEngine;

namespace GameMain2.Framework.Core.BehaviorTree
{
    public sealed class BehaviorTreeContext
    {
        public GameObject Owner { get; }
        public Transform Transform { get; }
        public BehaviorTreeBlackboard Blackboard { get; }
        public float DeltaTime { get; set; }

        /// <summary>创建一次行为树运行所需的上下文。</summary>
        public BehaviorTreeContext(GameObject owner, BehaviorTreeBlackboard blackboard = null)
        {
            Owner = owner;
            Transform = owner != null ? owner.transform : null;
            Blackboard = blackboard ?? new BehaviorTreeBlackboard();
        }
    }
}
```

- [ ] **Step 4: Run the tests and verify they pass**

Run:

```bash
$CLI test run --mode EditMode --group-name Game.Tests.EditMode.BehaviorTreeBlackboardEditModeTests --timeout 120000
```

Expected: all tests in `BehaviorTreeBlackboardEditModeTests` pass.

- [ ] **Step 5: Commit Task 1**

```bash
git add Assets/Game/Editor/BehaviorTreeBlackboardEditModeTests.cs Assets/Framework/Core/BehaviorTree/BehaviorTreeStatus.cs Assets/Framework/Core/BehaviorTree/BehaviorTreeBlackboard.cs Assets/Framework/Core/BehaviorTree/BehaviorTreeContext.cs
git commit -m "feat: add behavior tree context and blackboard"
```

---

### Task 2: Tree Asset, Runtime Node, And Runner

**Files:**
- Create: `Assets/Game/Editor/BehaviorTreeRunnerEditModeTests.cs`
- Create: `Assets/Framework/Core/BehaviorTree/BehaviorTreeAsset.cs`
- Create: `Assets/Framework/Core/BehaviorTree/Assets/BehaviorTreeNodeAsset.cs`
- Create: `Assets/Framework/Core/BehaviorTree/Runtime/BehaviorTreeNode.cs`
- Create: `Assets/Framework/Core/BehaviorTree/BehaviorTreeRunner.cs`

- [ ] **Step 1: Write the failing runner tests**

Create `Assets/Game/Editor/BehaviorTreeRunnerEditModeTests.cs`:

```csharp
using GameMain2.Framework.Core.BehaviorTree;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    public sealed class BehaviorTreeRunnerEditModeTests
    {
        /// <summary>验证空根节点的行为树不会执行成功。</summary>
        [Test]
        public void Runner_ReturnsFailureWhenRootMissing()
        {
            GameObject owner = new GameObject("Owner");
            BehaviorTreeAsset tree = ScriptableObject.CreateInstance<BehaviorTreeAsset>();
            try
            {
                BehaviorTreeRunner runner = new BehaviorTreeRunner(tree, new BehaviorTreeContext(owner));

                BehaviorTreeStatus status = runner.Tick(0.1f);

                Assert.AreEqual(BehaviorTreeStatus.Failure, status);
                Assert.IsFalse(runner.IsInitialized);
            }
            finally
            {
                Object.DestroyImmediate(tree);
                Object.DestroyImmediate(owner);
            }
        }

        /// <summary>验证两个 Runner 共享同一资产时不会共享运行时进度。</summary>
        [Test]
        public void Runner_DoesNotShareRuntimeStateBetweenInstances()
        {
            GameObject ownerA = new GameObject("OwnerA");
            GameObject ownerB = new GameObject("OwnerB");
            BehaviorTreeAsset tree = ScriptableObject.CreateInstance<BehaviorTreeAsset>();
            StatefulRunningNodeAsset node = ScriptableObject.CreateInstance<StatefulRunningNodeAsset>();
            try
            {
                node.SetRunningTicks(1);
                tree.SetRoot(node);

                BehaviorTreeRunner runnerA = new BehaviorTreeRunner(tree, new BehaviorTreeContext(ownerA));
                BehaviorTreeRunner runnerB = new BehaviorTreeRunner(tree, new BehaviorTreeContext(ownerB));

                Assert.AreEqual(BehaviorTreeStatus.Running, runnerA.Tick(0.1f));
                Assert.AreEqual(BehaviorTreeStatus.Success, runnerA.Tick(0.1f));
                Assert.AreEqual(BehaviorTreeStatus.Running, runnerB.Tick(0.1f));
            }
            finally
            {
                Object.DestroyImmediate(node);
                Object.DestroyImmediate(tree);
                Object.DestroyImmediate(ownerA);
                Object.DestroyImmediate(ownerB);
            }
        }

        private sealed class StatefulRunningNodeAsset : BehaviorTreeNodeAsset
        {
            private int runningTicks;

            /// <summary>配置该测试节点返回 Running 的次数。</summary>
            public void SetRunningTicks(int value)
            {
                runningTicks = value;
            }

            /// <summary>为每个 Runner 创建独立的运行时节点。</summary>
            public override BehaviorTreeNode CreateRuntimeNode()
            {
                return new StatefulRunningNode(this, runningTicks);
            }
        }

        private sealed class StatefulRunningNode : BehaviorTreeNode
        {
            private readonly int runningTicks;
            private int tickCount;

            /// <summary>创建带独立计数器的测试运行时节点。</summary>
            public StatefulRunningNode(BehaviorTreeNodeAsset asset, int runningTicks) : base(asset)
            {
                this.runningTicks = runningTicks;
            }

            /// <summary>前若干次返回 Running，之后返回 Success。</summary>
            public override BehaviorTreeStatus Tick(BehaviorTreeContext context)
            {
                tickCount++;
                return tickCount <= runningTicks ? BehaviorTreeStatus.Running : BehaviorTreeStatus.Success;
            }

            /// <summary>重置测试节点的运行时计数。</summary>
            public override void Reset()
            {
                tickCount = 0;
            }
        }
    }
}
```

- [ ] **Step 2: Run the tests and verify they fail**

Run:

```bash
$CLI test run --mode EditMode --group-name Game.Tests.EditMode.BehaviorTreeRunnerEditModeTests --timeout 120000
```

Expected: fails because `BehaviorTreeAsset`, `BehaviorTreeNodeAsset`, `BehaviorTreeNode`, and `BehaviorTreeRunner` do not exist yet.

- [ ] **Step 3: Implement asset, runtime node, and runner**

Create `Assets/Framework/Core/BehaviorTree/BehaviorTreeAsset.cs`:

```csharp
using UnityEngine;

namespace GameMain2.Framework.Core.BehaviorTree
{
    [CreateAssetMenu(fileName = "BehaviorTree", menuName = "Game/Behavior Tree/Behavior Tree")]
    public sealed class BehaviorTreeAsset : ScriptableObject
    {
        [SerializeField] private BehaviorTreeNodeAsset root;

        public BehaviorTreeNodeAsset Root => root;

        /// <summary>设置根节点，供测试和编辑器辅助流程使用。</summary>
        public void SetRoot(BehaviorTreeNodeAsset value)
        {
            root = value;
        }
    }
}
```

Create `Assets/Framework/Core/BehaviorTree/Assets/BehaviorTreeNodeAsset.cs`:

```csharp
using UnityEngine;

namespace GameMain2.Framework.Core.BehaviorTree
{
    public abstract class BehaviorTreeNodeAsset : ScriptableObject
    {
        [SerializeField] private string nodeName;

        public string NodeName => string.IsNullOrEmpty(nodeName) ? name : nodeName;

        /// <summary>创建该资产对应的运行时节点实例。</summary>
        public abstract BehaviorTreeNode CreateRuntimeNode();
    }
}
```

Create `Assets/Framework/Core/BehaviorTree/Runtime/BehaviorTreeNode.cs`:

```csharp
namespace GameMain2.Framework.Core.BehaviorTree
{
    public abstract class BehaviorTreeNode
    {
        public BehaviorTreeNodeAsset Asset { get; }

        /// <summary>记录运行时节点对应的配置资产。</summary>
        protected BehaviorTreeNode(BehaviorTreeNodeAsset asset)
        {
            Asset = asset;
        }

        /// <summary>执行一帧节点逻辑并返回行为树状态。</summary>
        public abstract BehaviorTreeStatus Tick(BehaviorTreeContext context);

        /// <summary>清理节点内部运行时状态。</summary>
        public virtual void Reset() { }
    }
}
```

Create `Assets/Framework/Core/BehaviorTree/BehaviorTreeRunner.cs`:

```csharp
using UnityEngine;

namespace GameMain2.Framework.Core.BehaviorTree
{
    public sealed class BehaviorTreeRunner
    {
        private BehaviorTreeAsset treeAsset;
        private readonly BehaviorTreeContext context;
        private BehaviorTreeNode rootNode;
        private bool isInitialized;

        public bool IsInitialized => isInitialized;
        public BehaviorTreeContext Context => context;

        /// <summary>创建一个行为树运行器，运行器持有独立运行时状态。</summary>
        public BehaviorTreeRunner(BehaviorTreeAsset treeAsset, BehaviorTreeContext context)
        {
            this.treeAsset = treeAsset;
            this.context = context;
        }

        /// <summary>初始化行为树并创建运行时根节点。</summary>
        public bool Start()
        {
            if (context == null || context.Owner == null)
            {
                Debug.LogError("BehaviorTreeRunner requires a valid context owner.");
                isInitialized = false;
                return false;
            }

            if (treeAsset == null || treeAsset.Root == null)
            {
                Debug.LogError("BehaviorTreeRunner requires a behavior tree asset with root node.");
                isInitialized = false;
                return false;
            }

            rootNode = treeAsset.Root.CreateRuntimeNode();
            isInitialized = rootNode != null;
            return isInitialized;
        }

        /// <summary>执行一帧行为树逻辑。</summary>
        public BehaviorTreeStatus Tick(float deltaTime)
        {
            if (!isInitialized && !Start())
            {
                return BehaviorTreeStatus.Failure;
            }

            context.DeltaTime = deltaTime;
            return rootNode.Tick(context);
        }

        /// <summary>重置整棵树的运行时状态。</summary>
        public void Reset()
        {
            rootNode?.Reset();
        }

        /// <summary>切换行为树资产并清理旧树运行状态。</summary>
        public void SetTree(BehaviorTreeAsset value)
        {
            Reset();
            treeAsset = value;
            rootNode = null;
            isInitialized = false;
        }
    }
}
```

- [ ] **Step 4: Run the tests and verify they pass**

Run:

```bash
$CLI test run --mode EditMode --group-name Game.Tests.EditMode.BehaviorTreeRunnerEditModeTests --timeout 120000
```

Expected: both runner tests pass.

- [ ] **Step 5: Commit Task 2**

```bash
git add Assets/Game/Editor/BehaviorTreeRunnerEditModeTests.cs Assets/Framework/Core/BehaviorTree/BehaviorTreeAsset.cs Assets/Framework/Core/BehaviorTree/Assets/BehaviorTreeNodeAsset.cs Assets/Framework/Core/BehaviorTree/Runtime/BehaviorTreeNode.cs Assets/Framework/Core/BehaviorTree/BehaviorTreeRunner.cs
git commit -m "feat: add behavior tree runner"
```

---

### Task 3: Condition And Action Leaf Bases

**Files:**
- Create: `Assets/Game/Editor/BehaviorTreeLeafNodeEditModeTests.cs`
- Create: `Assets/Framework/Core/BehaviorTree/Assets/ConditionNodeAsset.cs`
- Create: `Assets/Framework/Core/BehaviorTree/Assets/ActionNodeAsset.cs`

- [ ] **Step 1: Write the failing leaf-node tests**

Create `Assets/Game/Editor/BehaviorTreeLeafNodeEditModeTests.cs`:

```csharp
using GameMain2.Framework.Core.BehaviorTree;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    public sealed class BehaviorTreeLeafNodeEditModeTests
    {
        /// <summary>验证条件节点把布尔判断转换为成功或失败。</summary>
        [Test]
        public void ConditionNode_ReturnsSuccessOrFailureFromEvaluation()
        {
            GameObject owner = new GameObject("Owner");
            TestConditionNodeAsset condition = ScriptableObject.CreateInstance<TestConditionNodeAsset>();
            try
            {
                condition.SetResult(true);
                Assert.AreEqual(BehaviorTreeStatus.Success, condition.CreateRuntimeNode().Tick(new BehaviorTreeContext(owner)));

                condition.SetResult(false);
                Assert.AreEqual(BehaviorTreeStatus.Failure, condition.CreateRuntimeNode().Tick(new BehaviorTreeContext(owner)));
            }
            finally
            {
                Object.DestroyImmediate(condition);
                Object.DestroyImmediate(owner);
            }
        }

        /// <summary>验证动作节点可以直接返回三态结果。</summary>
        [Test]
        public void ActionNode_ReturnsConfiguredStatus()
        {
            GameObject owner = new GameObject("Owner");
            TestActionNodeAsset action = ScriptableObject.CreateInstance<TestActionNodeAsset>();
            try
            {
                action.SetStatus(BehaviorTreeStatus.Running);
                Assert.AreEqual(BehaviorTreeStatus.Running, action.CreateRuntimeNode().Tick(new BehaviorTreeContext(owner)));

                action.SetStatus(BehaviorTreeStatus.Success);
                Assert.AreEqual(BehaviorTreeStatus.Success, action.CreateRuntimeNode().Tick(new BehaviorTreeContext(owner)));
            }
            finally
            {
                Object.DestroyImmediate(action);
                Object.DestroyImmediate(owner);
            }
        }

        private sealed class TestConditionNodeAsset : ConditionNodeAsset
        {
            private bool result;

            /// <summary>设置测试条件的返回值。</summary>
            public void SetResult(bool value)
            {
                result = value;
            }

            /// <summary>返回测试配置的条件结果。</summary>
            protected override bool Evaluate(BehaviorTreeContext context)
            {
                return result;
            }
        }

        private sealed class TestActionNodeAsset : ActionNodeAsset
        {
            private BehaviorTreeStatus status;

            /// <summary>设置测试动作节点的返回状态。</summary>
            public void SetStatus(BehaviorTreeStatus value)
            {
                status = value;
            }

            /// <summary>返回测试配置的动作状态。</summary>
            protected override BehaviorTreeStatus Execute(BehaviorTreeContext context)
            {
                return status;
            }
        }
    }
}
```

- [ ] **Step 2: Run the tests and verify they fail**

Run:

```bash
$CLI test run --mode EditMode --group-name Game.Tests.EditMode.BehaviorTreeLeafNodeEditModeTests --timeout 120000
```

Expected: fails because `ConditionNodeAsset` and `ActionNodeAsset` do not exist yet.

- [ ] **Step 3: Implement leaf node bases**

Create `Assets/Framework/Core/BehaviorTree/Assets/ConditionNodeAsset.cs`:

```csharp
namespace GameMain2.Framework.Core.BehaviorTree
{
    public abstract class ConditionNodeAsset : BehaviorTreeNodeAsset
    {
        /// <summary>创建条件节点的默认运行时实例。</summary>
        public override BehaviorTreeNode CreateRuntimeNode()
        {
            return new ConditionNode(this);
        }

        /// <summary>执行业务条件判断，true 表示成功，false 表示失败。</summary>
        protected abstract bool Evaluate(BehaviorTreeContext context);

        private sealed class ConditionNode : BehaviorTreeNode
        {
            private readonly ConditionNodeAsset asset;

            /// <summary>保存条件节点资产引用。</summary>
            public ConditionNode(ConditionNodeAsset asset) : base(asset)
            {
                this.asset = asset;
            }

            /// <summary>执行条件判断并转换为行为树状态。</summary>
            public override BehaviorTreeStatus Tick(BehaviorTreeContext context)
            {
                return asset.Evaluate(context) ? BehaviorTreeStatus.Success : BehaviorTreeStatus.Failure;
            }
        }
    }
}
```

Create `Assets/Framework/Core/BehaviorTree/Assets/ActionNodeAsset.cs`:

```csharp
namespace GameMain2.Framework.Core.BehaviorTree
{
    public abstract class ActionNodeAsset : BehaviorTreeNodeAsset
    {
        /// <summary>创建动作节点的默认运行时实例。</summary>
        public override BehaviorTreeNode CreateRuntimeNode()
        {
            return new ActionNode(this);
        }

        /// <summary>执行一帧业务动作并返回行为树状态。</summary>
        protected abstract BehaviorTreeStatus Execute(BehaviorTreeContext context);

        private sealed class ActionNode : BehaviorTreeNode
        {
            private readonly ActionNodeAsset asset;

            /// <summary>保存动作节点资产引用。</summary>
            public ActionNode(ActionNodeAsset asset) : base(asset)
            {
                this.asset = asset;
            }

            /// <summary>执行一帧动作逻辑。</summary>
            public override BehaviorTreeStatus Tick(BehaviorTreeContext context)
            {
                return asset.Execute(context);
            }
        }
    }
}
```

- [ ] **Step 4: Run the tests and verify they pass**

Run:

```bash
$CLI test run --mode EditMode --group-name Game.Tests.EditMode.BehaviorTreeLeafNodeEditModeTests --timeout 120000
```

Expected: all leaf-node tests pass.

- [ ] **Step 5: Commit Task 3**

```bash
git add Assets/Game/Editor/BehaviorTreeLeafNodeEditModeTests.cs Assets/Framework/Core/BehaviorTree/Assets/ConditionNodeAsset.cs Assets/Framework/Core/BehaviorTree/Assets/ActionNodeAsset.cs
git commit -m "feat: add behavior tree leaf nodes"
```

---

### Task 4: Selector And Sequence Composite Nodes

**Files:**
- Create: `Assets/Game/Editor/BehaviorTreeCompositeNodeEditModeTests.cs`
- Create: `Assets/Framework/Core/BehaviorTree/Assets/CompositeNodeAsset.cs`
- Create: `Assets/Framework/Core/BehaviorTree/Nodes/SelectorNodeAsset.cs`
- Create: `Assets/Framework/Core/BehaviorTree/Nodes/SequenceNodeAsset.cs`

- [ ] **Step 1: Write the failing composite tests**

Create `Assets/Game/Editor/BehaviorTreeCompositeNodeEditModeTests.cs`:

```csharp
using GameMain2.Framework.Core.BehaviorTree;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    public sealed class BehaviorTreeCompositeNodeEditModeTests
    {
        /// <summary>验证 Selector 遇到第一个成功子节点后不会继续执行后续子节点。</summary>
        [Test]
        public void Selector_ReturnsFirstSuccessWithoutTickingLaterChild()
        {
            GameObject owner = new GameObject("Owner");
            SelectorNodeAsset selector = ScriptableObject.CreateInstance<SelectorNodeAsset>();
            TestStatusNodeAsset first = ScriptableObject.CreateInstance<TestStatusNodeAsset>();
            TestStatusNodeAsset second = ScriptableObject.CreateInstance<TestStatusNodeAsset>();
            try
            {
                first.SetStatuses(BehaviorTreeStatus.Success);
                second.SetStatuses(BehaviorTreeStatus.Success);
                selector.SetChildren(first, second);

                BehaviorTreeStatus status = selector.CreateRuntimeNode().Tick(new BehaviorTreeContext(owner));

                Assert.AreEqual(BehaviorTreeStatus.Success, status);
                Assert.AreEqual(1, first.RuntimeTickCount);
                Assert.AreEqual(0, second.RuntimeTickCount);
            }
            finally
            {
                Object.DestroyImmediate(second);
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(selector);
                Object.DestroyImmediate(owner);
            }
        }

        /// <summary>验证 Selector 在 Running 后下一帧继续当前子节点。</summary>
        [Test]
        public void Selector_ContinuesRunningChildOnNextTick()
        {
            GameObject owner = new GameObject("Owner");
            SelectorNodeAsset selector = ScriptableObject.CreateInstance<SelectorNodeAsset>();
            TestStatusNodeAsset first = ScriptableObject.CreateInstance<TestStatusNodeAsset>();
            TestStatusNodeAsset second = ScriptableObject.CreateInstance<TestStatusNodeAsset>();
            try
            {
                first.SetStatuses(BehaviorTreeStatus.Running, BehaviorTreeStatus.Failure);
                second.SetStatuses(BehaviorTreeStatus.Success);
                selector.SetChildren(first, second);
                BehaviorTreeNode runtime = selector.CreateRuntimeNode();
                BehaviorTreeContext context = new BehaviorTreeContext(owner);

                Assert.AreEqual(BehaviorTreeStatus.Running, runtime.Tick(context));
                Assert.AreEqual(BehaviorTreeStatus.Success, runtime.Tick(context));
                Assert.AreEqual(2, first.RuntimeTickCount);
                Assert.AreEqual(1, second.RuntimeTickCount);
            }
            finally
            {
                Object.DestroyImmediate(second);
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(selector);
                Object.DestroyImmediate(owner);
            }
        }

        /// <summary>验证 Sequence 在 Running 后下一帧继续当前子节点。</summary>
        [Test]
        public void Sequence_ContinuesRunningChildOnNextTick()
        {
            GameObject owner = new GameObject("Owner");
            SequenceNodeAsset sequence = ScriptableObject.CreateInstance<SequenceNodeAsset>();
            TestStatusNodeAsset first = ScriptableObject.CreateInstance<TestStatusNodeAsset>();
            TestStatusNodeAsset second = ScriptableObject.CreateInstance<TestStatusNodeAsset>();
            try
            {
                first.SetStatuses(BehaviorTreeStatus.Running, BehaviorTreeStatus.Success);
                second.SetStatuses(BehaviorTreeStatus.Success);
                sequence.SetChildren(first, second);
                BehaviorTreeNode runtime = sequence.CreateRuntimeNode();
                BehaviorTreeContext context = new BehaviorTreeContext(owner);

                Assert.AreEqual(BehaviorTreeStatus.Running, runtime.Tick(context));
                Assert.AreEqual(BehaviorTreeStatus.Success, runtime.Tick(context));
                Assert.AreEqual(2, first.RuntimeTickCount);
                Assert.AreEqual(1, second.RuntimeTickCount);
            }
            finally
            {
                Object.DestroyImmediate(second);
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(sequence);
                Object.DestroyImmediate(owner);
            }
        }

        private sealed class TestStatusNodeAsset : BehaviorTreeNodeAsset
        {
            private BehaviorTreeStatus[] statuses;
            public int RuntimeTickCount { get; private set; }

            /// <summary>配置测试节点每次 Tick 的返回状态。</summary>
            public void SetStatuses(params BehaviorTreeStatus[] values)
            {
                statuses = values;
                RuntimeTickCount = 0;
            }

            /// <summary>创建带独立状态序列的运行时节点。</summary>
            public override BehaviorTreeNode CreateRuntimeNode()
            {
                return new TestStatusNode(this, statuses);
            }

            /// <summary>记录运行时节点被执行的次数。</summary>
            public void AddRuntimeTick()
            {
                RuntimeTickCount++;
            }
        }

        private sealed class TestStatusNode : BehaviorTreeNode
        {
            private readonly TestStatusNodeAsset ownerAsset;
            private readonly BehaviorTreeStatus[] statuses;
            private int index;

            /// <summary>创建按固定状态序列返回的测试节点。</summary>
            public TestStatusNode(TestStatusNodeAsset asset, BehaviorTreeStatus[] statuses) : base(asset)
            {
                ownerAsset = asset;
                this.statuses = statuses;
            }

            /// <summary>返回当前序列状态，序列耗尽后持续返回最后一个状态。</summary>
            public override BehaviorTreeStatus Tick(BehaviorTreeContext context)
            {
                ownerAsset.AddRuntimeTick();
                int statusIndex = Mathf.Min(index, statuses.Length - 1);
                index++;
                return statuses[statusIndex];
            }

            /// <summary>重置状态序列索引。</summary>
            public override void Reset()
            {
                index = 0;
            }
        }
    }
}
```

- [ ] **Step 2: Run the tests and verify they fail**

Run:

```bash
$CLI test run --mode EditMode --group-name Game.Tests.EditMode.BehaviorTreeCompositeNodeEditModeTests --timeout 120000
```

Expected: fails because composite node assets do not exist yet.

- [ ] **Step 3: Implement composite node assets**

Create `Assets/Framework/Core/BehaviorTree/Assets/CompositeNodeAsset.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace GameMain2.Framework.Core.BehaviorTree
{
    public abstract class CompositeNodeAsset : BehaviorTreeNodeAsset
    {
        [SerializeField] private List<BehaviorTreeNodeAsset> children = new List<BehaviorTreeNodeAsset>();

        public IReadOnlyList<BehaviorTreeNodeAsset> Children => children;

        /// <summary>设置子节点列表，供测试和编辑器辅助流程使用。</summary>
        public void SetChildren(params BehaviorTreeNodeAsset[] values)
        {
            children.Clear();
            if (values != null)
            {
                children.AddRange(values);
            }
        }

        /// <summary>递归创建所有有效子节点的运行时实例。</summary>
        protected List<BehaviorTreeNode> CreateRuntimeChildren()
        {
            List<BehaviorTreeNode> runtimeChildren = new List<BehaviorTreeNode>();
            foreach (BehaviorTreeNodeAsset child in children)
            {
                if (child == null)
                {
                    Debug.LogWarning($"{name} has an empty child node.");
                    continue;
                }

                runtimeChildren.Add(child.CreateRuntimeNode());
            }

            return runtimeChildren;
        }
    }
}
```

Create `Assets/Framework/Core/BehaviorTree/Nodes/SelectorNodeAsset.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace GameMain2.Framework.Core.BehaviorTree
{
    [CreateAssetMenu(fileName = "SelectorNode", menuName = "Game/Behavior Tree/Selector")]
    public sealed class SelectorNodeAsset : CompositeNodeAsset
    {
        /// <summary>创建 Selector 的运行时实例。</summary>
        public override BehaviorTreeNode CreateRuntimeNode()
        {
            return new SelectorNode(this, CreateRuntimeChildren());
        }

        private sealed class SelectorNode : BehaviorTreeNode
        {
            private readonly List<BehaviorTreeNode> children;
            private int currentIndex;

            /// <summary>保存 Selector 子节点运行时实例。</summary>
            public SelectorNode(BehaviorTreeNodeAsset asset, List<BehaviorTreeNode> children) : base(asset)
            {
                this.children = children;
            }

            /// <summary>按顺序寻找第一个成功或运行中的子节点。</summary>
            public override BehaviorTreeStatus Tick(BehaviorTreeContext context)
            {
                while (currentIndex < children.Count)
                {
                    BehaviorTreeStatus status = children[currentIndex].Tick(context);
                    if (status == BehaviorTreeStatus.Running)
                    {
                        return BehaviorTreeStatus.Running;
                    }

                    if (status == BehaviorTreeStatus.Success)
                    {
                        Reset();
                        return BehaviorTreeStatus.Success;
                    }

                    currentIndex++;
                }

                Reset();
                return BehaviorTreeStatus.Failure;
            }

            /// <summary>重置 Selector 当前索引和所有子节点状态。</summary>
            public override void Reset()
            {
                currentIndex = 0;
                foreach (BehaviorTreeNode child in children)
                {
                    child.Reset();
                }
            }
        }
    }
}
```

Create `Assets/Framework/Core/BehaviorTree/Nodes/SequenceNodeAsset.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace GameMain2.Framework.Core.BehaviorTree
{
    [CreateAssetMenu(fileName = "SequenceNode", menuName = "Game/Behavior Tree/Sequence")]
    public sealed class SequenceNodeAsset : CompositeNodeAsset
    {
        /// <summary>创建 Sequence 的运行时实例。</summary>
        public override BehaviorTreeNode CreateRuntimeNode()
        {
            return new SequenceNode(this, CreateRuntimeChildren());
        }

        private sealed class SequenceNode : BehaviorTreeNode
        {
            private readonly List<BehaviorTreeNode> children;
            private int currentIndex;

            /// <summary>保存 Sequence 子节点运行时实例。</summary>
            public SequenceNode(BehaviorTreeNodeAsset asset, List<BehaviorTreeNode> children) : base(asset)
            {
                this.children = children;
            }

            /// <summary>按顺序执行子节点，直到失败、运行中或全部成功。</summary>
            public override BehaviorTreeStatus Tick(BehaviorTreeContext context)
            {
                while (currentIndex < children.Count)
                {
                    BehaviorTreeStatus status = children[currentIndex].Tick(context);
                    if (status == BehaviorTreeStatus.Running)
                    {
                        return BehaviorTreeStatus.Running;
                    }

                    if (status == BehaviorTreeStatus.Failure)
                    {
                        Reset();
                        return BehaviorTreeStatus.Failure;
                    }

                    currentIndex++;
                }

                Reset();
                return BehaviorTreeStatus.Success;
            }

            /// <summary>重置 Sequence 当前索引和所有子节点状态。</summary>
            public override void Reset()
            {
                currentIndex = 0;
                foreach (BehaviorTreeNode child in children)
                {
                    child.Reset();
                }
            }
        }
    }
}
```

- [ ] **Step 4: Run the tests and verify they pass**

Run:

```bash
$CLI test run --mode EditMode --group-name Game.Tests.EditMode.BehaviorTreeCompositeNodeEditModeTests --timeout 120000
```

Expected: all composite-node tests pass.

- [ ] **Step 5: Commit Task 4**

```bash
git add Assets/Game/Editor/BehaviorTreeCompositeNodeEditModeTests.cs Assets/Framework/Core/BehaviorTree/Assets/CompositeNodeAsset.cs Assets/Framework/Core/BehaviorTree/Nodes/SelectorNodeAsset.cs Assets/Framework/Core/BehaviorTree/Nodes/SequenceNodeAsset.cs
git commit -m "feat: add behavior tree composite nodes"
```

---

### Task 5: Decorator Nodes

**Files:**
- Create: `Assets/Game/Editor/BehaviorTreeDecoratorNodeEditModeTests.cs`
- Create: `Assets/Framework/Core/BehaviorTree/Assets/DecoratorNodeAsset.cs`
- Create: `Assets/Framework/Core/BehaviorTree/Nodes/InverterNodeAsset.cs`
- Create: `Assets/Framework/Core/BehaviorTree/Nodes/AlwaysSuccessNodeAsset.cs`
- Create: `Assets/Framework/Core/BehaviorTree/Nodes/AlwaysFailureNodeAsset.cs`

- [ ] **Step 1: Write the failing decorator tests**

Create `Assets/Game/Editor/BehaviorTreeDecoratorNodeEditModeTests.cs`:

```csharp
using GameMain2.Framework.Core.BehaviorTree;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    public sealed class BehaviorTreeDecoratorNodeEditModeTests
    {
        /// <summary>验证 Inverter 反转终态并保留 Running。</summary>
        [Test]
        public void Inverter_InvertsTerminalStatusAndKeepsRunning()
        {
            AssertDecoratorStatus<InverterNodeAsset>(BehaviorTreeStatus.Success, BehaviorTreeStatus.Failure);
            AssertDecoratorStatus<InverterNodeAsset>(BehaviorTreeStatus.Failure, BehaviorTreeStatus.Success);
            AssertDecoratorStatus<InverterNodeAsset>(BehaviorTreeStatus.Running, BehaviorTreeStatus.Running);
        }

        /// <summary>验证 AlwaysSuccess 保留 Running 并把终态改成 Success。</summary>
        [Test]
        public void AlwaysSuccess_RewritesTerminalStatusAndKeepsRunning()
        {
            AssertDecoratorStatus<AlwaysSuccessNodeAsset>(BehaviorTreeStatus.Success, BehaviorTreeStatus.Success);
            AssertDecoratorStatus<AlwaysSuccessNodeAsset>(BehaviorTreeStatus.Failure, BehaviorTreeStatus.Success);
            AssertDecoratorStatus<AlwaysSuccessNodeAsset>(BehaviorTreeStatus.Running, BehaviorTreeStatus.Running);
        }

        /// <summary>验证 AlwaysFailure 保留 Running 并把终态改成 Failure。</summary>
        [Test]
        public void AlwaysFailure_RewritesTerminalStatusAndKeepsRunning()
        {
            AssertDecoratorStatus<AlwaysFailureNodeAsset>(BehaviorTreeStatus.Success, BehaviorTreeStatus.Failure);
            AssertDecoratorStatus<AlwaysFailureNodeAsset>(BehaviorTreeStatus.Failure, BehaviorTreeStatus.Failure);
            AssertDecoratorStatus<AlwaysFailureNodeAsset>(BehaviorTreeStatus.Running, BehaviorTreeStatus.Running);
        }

        /// <summary>验证指定装饰器对子节点状态的转换结果。</summary>
        private static void AssertDecoratorStatus<T>(BehaviorTreeStatus childStatus, BehaviorTreeStatus expectedStatus)
            where T : DecoratorNodeAsset
        {
            GameObject owner = new GameObject("Owner");
            T decorator = ScriptableObject.CreateInstance<T>();
            TestStatusNodeAsset child = ScriptableObject.CreateInstance<TestStatusNodeAsset>();
            try
            {
                child.SetStatus(childStatus);
                decorator.SetChild(child);

                BehaviorTreeStatus status = decorator.CreateRuntimeNode().Tick(new BehaviorTreeContext(owner));

                Assert.AreEqual(expectedStatus, status);
            }
            finally
            {
                Object.DestroyImmediate(child);
                Object.DestroyImmediate(decorator);
                Object.DestroyImmediate(owner);
            }
        }

        private sealed class TestStatusNodeAsset : BehaviorTreeNodeAsset
        {
            private BehaviorTreeStatus status;

            /// <summary>设置测试子节点返回状态。</summary>
            public void SetStatus(BehaviorTreeStatus value)
            {
                status = value;
            }

            /// <summary>创建返回固定状态的运行时节点。</summary>
            public override BehaviorTreeNode CreateRuntimeNode()
            {
                return new TestStatusNode(this, status);
            }
        }

        private sealed class TestStatusNode : BehaviorTreeNode
        {
            private readonly BehaviorTreeStatus status;

            /// <summary>创建固定返回状态的测试节点。</summary>
            public TestStatusNode(BehaviorTreeNodeAsset asset, BehaviorTreeStatus status) : base(asset)
            {
                this.status = status;
            }

            /// <summary>返回配置好的测试状态。</summary>
            public override BehaviorTreeStatus Tick(BehaviorTreeContext context)
            {
                return status;
            }
        }
    }
}
```

- [ ] **Step 2: Run the tests and verify they fail**

Run:

```bash
$CLI test run --mode EditMode --group-name Game.Tests.EditMode.BehaviorTreeDecoratorNodeEditModeTests --timeout 120000
```

Expected: fails because decorator node assets do not exist yet.

- [ ] **Step 3: Implement decorator nodes**

Create `Assets/Framework/Core/BehaviorTree/Assets/DecoratorNodeAsset.cs`:

```csharp
using UnityEngine;

namespace GameMain2.Framework.Core.BehaviorTree
{
    public abstract class DecoratorNodeAsset : BehaviorTreeNodeAsset
    {
        [SerializeField] private BehaviorTreeNodeAsset child;

        public BehaviorTreeNodeAsset Child => child;

        /// <summary>设置装饰器子节点，供测试和编辑器辅助流程使用。</summary>
        public void SetChild(BehaviorTreeNodeAsset value)
        {
            child = value;
        }

        /// <summary>创建装饰器子节点的运行时实例。</summary>
        protected BehaviorTreeNode CreateRuntimeChild()
        {
            if (child == null)
            {
                Debug.LogWarning($"{name} requires a child node.");
                return null;
            }

            return child.CreateRuntimeNode();
        }
    }
}
```

Create `Assets/Framework/Core/BehaviorTree/Nodes/InverterNodeAsset.cs`:

```csharp
using UnityEngine;

namespace GameMain2.Framework.Core.BehaviorTree
{
    [CreateAssetMenu(fileName = "InverterNode", menuName = "Game/Behavior Tree/Inverter")]
    public sealed class InverterNodeAsset : DecoratorNodeAsset
    {
        /// <summary>创建 Inverter 的运行时实例。</summary>
        public override BehaviorTreeNode CreateRuntimeNode()
        {
            return new InverterNode(this, CreateRuntimeChild());
        }

        private sealed class InverterNode : BehaviorTreeNode
        {
            private readonly BehaviorTreeNode child;

            /// <summary>保存被反转的子节点。</summary>
            public InverterNode(BehaviorTreeNodeAsset asset, BehaviorTreeNode child) : base(asset)
            {
                this.child = child;
            }

            /// <summary>反转子节点的成功和失败状态。</summary>
            public override BehaviorTreeStatus Tick(BehaviorTreeContext context)
            {
                if (child == null)
                {
                    return BehaviorTreeStatus.Failure;
                }

                BehaviorTreeStatus status = child.Tick(context);
                if (status == BehaviorTreeStatus.Success)
                {
                    return BehaviorTreeStatus.Failure;
                }

                return status == BehaviorTreeStatus.Failure ? BehaviorTreeStatus.Success : BehaviorTreeStatus.Running;
            }

            /// <summary>重置子节点运行状态。</summary>
            public override void Reset()
            {
                child?.Reset();
            }
        }
    }
}
```

Create `Assets/Framework/Core/BehaviorTree/Nodes/AlwaysSuccessNodeAsset.cs`:

```csharp
using UnityEngine;

namespace GameMain2.Framework.Core.BehaviorTree
{
    [CreateAssetMenu(fileName = "AlwaysSuccessNode", menuName = "Game/Behavior Tree/Always Success")]
    public sealed class AlwaysSuccessNodeAsset : DecoratorNodeAsset
    {
        /// <summary>创建 AlwaysSuccess 的运行时实例。</summary>
        public override BehaviorTreeNode CreateRuntimeNode()
        {
            return new AlwaysSuccessNode(this, CreateRuntimeChild());
        }

        private sealed class AlwaysSuccessNode : BehaviorTreeNode
        {
            private readonly BehaviorTreeNode child;

            /// <summary>保存被改写结果的子节点。</summary>
            public AlwaysSuccessNode(BehaviorTreeNodeAsset asset, BehaviorTreeNode child) : base(asset)
            {
                this.child = child;
            }

            /// <summary>保留 Running，并把其它结果改为 Success。</summary>
            public override BehaviorTreeStatus Tick(BehaviorTreeContext context)
            {
                if (child == null)
                {
                    return BehaviorTreeStatus.Failure;
                }

                BehaviorTreeStatus status = child.Tick(context);
                return status == BehaviorTreeStatus.Running ? BehaviorTreeStatus.Running : BehaviorTreeStatus.Success;
            }

            /// <summary>重置子节点运行状态。</summary>
            public override void Reset()
            {
                child?.Reset();
            }
        }
    }
}
```

Create `Assets/Framework/Core/BehaviorTree/Nodes/AlwaysFailureNodeAsset.cs`:

```csharp
using UnityEngine;

namespace GameMain2.Framework.Core.BehaviorTree
{
    [CreateAssetMenu(fileName = "AlwaysFailureNode", menuName = "Game/Behavior Tree/Always Failure")]
    public sealed class AlwaysFailureNodeAsset : DecoratorNodeAsset
    {
        /// <summary>创建 AlwaysFailure 的运行时实例。</summary>
        public override BehaviorTreeNode CreateRuntimeNode()
        {
            return new AlwaysFailureNode(this, CreateRuntimeChild());
        }

        private sealed class AlwaysFailureNode : BehaviorTreeNode
        {
            private readonly BehaviorTreeNode child;

            /// <summary>保存被改写结果的子节点。</summary>
            public AlwaysFailureNode(BehaviorTreeNodeAsset asset, BehaviorTreeNode child) : base(asset)
            {
                this.child = child;
            }

            /// <summary>保留 Running，并把其它结果改为 Failure。</summary>
            public override BehaviorTreeStatus Tick(BehaviorTreeContext context)
            {
                if (child == null)
                {
                    return BehaviorTreeStatus.Failure;
                }

                BehaviorTreeStatus status = child.Tick(context);
                return status == BehaviorTreeStatus.Running ? BehaviorTreeStatus.Running : BehaviorTreeStatus.Failure;
            }

            /// <summary>重置子节点运行状态。</summary>
            public override void Reset()
            {
                child?.Reset();
            }
        }
    }
}
```

- [ ] **Step 4: Run the tests and verify they pass**

Run:

```bash
$CLI test run --mode EditMode --group-name Game.Tests.EditMode.BehaviorTreeDecoratorNodeEditModeTests --timeout 120000
```

Expected: all decorator-node tests pass.

- [ ] **Step 5: Commit Task 5**

```bash
git add Assets/Game/Editor/BehaviorTreeDecoratorNodeEditModeTests.cs Assets/Framework/Core/BehaviorTree/Assets/DecoratorNodeAsset.cs Assets/Framework/Core/BehaviorTree/Nodes/InverterNodeAsset.cs Assets/Framework/Core/BehaviorTree/Nodes/AlwaysSuccessNodeAsset.cs Assets/Framework/Core/BehaviorTree/Nodes/AlwaysFailureNodeAsset.cs
git commit -m "feat: add behavior tree decorator nodes"
```

---

### Task 6: Full Verification

**Files:**
- No source file changes expected.

- [ ] **Step 1: Run all behavior tree EditMode tests**

Run:

```bash
$CLI test run --mode EditMode --group-name Game.Tests.EditMode.BehaviorTree --timeout 120000
```

Expected: all behavior tree EditMode tests pass. If the group filter does not match all fixtures in this Unity version, run each fixture explicitly:

```bash
$CLI test run --mode EditMode --group-name Game.Tests.EditMode.BehaviorTreeBlackboardEditModeTests --timeout 120000
$CLI test run --mode EditMode --group-name Game.Tests.EditMode.BehaviorTreeRunnerEditModeTests --timeout 120000
$CLI test run --mode EditMode --group-name Game.Tests.EditMode.BehaviorTreeLeafNodeEditModeTests --timeout 120000
$CLI test run --mode EditMode --group-name Game.Tests.EditMode.BehaviorTreeCompositeNodeEditModeTests --timeout 120000
$CLI test run --mode EditMode --group-name Game.Tests.EditMode.BehaviorTreeDecoratorNodeEditModeTests --timeout 120000
```

- [ ] **Step 2: Run Unity compile validation**

Run:

```bash
$CLI compile unity
```

Expected: Unity compile succeeds with no behavior-tree compile errors.

- [ ] **Step 3: Check git status**

Run:

```bash
git status --short
```

Expected: only unrelated pre-existing user changes remain outside the behavior tree files, or the behavior tree task files are clean after commits.

---

## Self-Review

Spec coverage:

- ScriptableObject tree assets: Task 2 creates `BehaviorTreeAsset` and `BehaviorTreeNodeAsset`.
- Per-runner runtime state: Task 2 test proves two runners do not share Running state.
- `Success` / `Failure` / `Running`: Task 1 creates enum, Tasks 2-5 exercise all states.
- Context and blackboard: Task 1 implements and tests both.
- Core nodes: Tasks 3-5 implement condition/action, selector/sequence, and three decorators.
- No enemy-specific logic: all files live under framework or generic EditMode test namespaces.
- Validation: Task 6 runs behavior tree tests and `$CLI compile unity`.

Placeholder scan:

- No placeholder red-flag terms or unspecified edge handling remains.
- Every code-changing step includes concrete file content.
- Commands include exact paths or fixture names and expected outcomes.

Type consistency:

- Namespace is consistently `GameMain2.Framework.Core.BehaviorTree`.
- Status enum name is consistently `BehaviorTreeStatus`.
- Runner API is consistently `Start()`, `Tick(float deltaTime)`, `Reset()`, and `SetTree(BehaviorTreeAsset value)`.
- Test helper methods match the production helper methods: `SetRoot`, `SetChildren`, and `SetChild`.
