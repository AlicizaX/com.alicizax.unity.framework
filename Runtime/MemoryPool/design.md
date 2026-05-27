# MemoryPool 自适应优化设计

## 目标约束

- 业务不需要手动大量 `Prewarm` / `Add`。
- 不依赖人为容量配置。
- 不要求业务使用 `TryAcquire`。
- 保留现有公开方法名，不移除入口。
- 常态命中路径 MemoryPool bookkeeping O(1) / 0GC；不包含 `Clear()` / `OnEvict()`，也不包含首次未知峰值 miss 的应急创建。
- 允许使用 `unsafe` / Native 容器优化元数据和调度结构。
- 首次未知峰值允许框架内部最小兜底分配，之后通过水位算法自动学习并吸收。
- 不依赖每次版本更新后的人工 Profile 导出。

## Public API 保留

继续公开：

```csharp
MemoryPool.Acquire<T>()
MemoryPool.Release<T>(T memory)

MemoryPool.Add<T>(int count)
MemoryPool.Add(Type type, int count)
MemoryPool.SetCapacity<T>(int softCapacity, int hardCapacity)

MemoryPool.Acquire(Type type)
MemoryPool.Release(IMemory memory)
```

语义调整：

- `Add`：高级容量干预入口，用于提高目标水位；业务常规流程不需要调用。
- `SetCapacity`：调整策略上下限，不在当前调用中强制大规模创建、搬迁或压缩。
- `Acquire(Type)`：公开保留；动态入口按需获取句柄，不扫描全部池类型。
- `Release(IMemory)`：公开保留；只接受 `MemoryObject` 实例并通过对象内 OwnerHandle 直达池；非 `MemoryObject` 必须拒绝，不能按类型兜底。

业务推荐路径仍然是：

```csharp
var item = MemoryPool.Acquire<Foo>();
MemoryPool.Release(item);
```

语义破坏点必须显式接受：

```text
where T : class, IMemory, new()
```

升级为：

```text
where T : MemoryObject, new()
```

这是源码级破坏性变更。公开方法名保留，但 Runtime 池类型必须迁移到 `MemoryObject`。旧 `IMemory` 类型如果不继承 `MemoryObject`，不再作为 MemoryPool 池对象支持。

`Add` 语义也发生变化：从“立即同步填充指定数量”改成“提高目标水位”。业务常规流程不应该依赖 `Add` 后立即有指定数量可用对象。

MemoryPool 只保留一个池实现：

```text
MemoryPool<T> where T : MemoryObject, new()
```

不保留旧 `IMemory` 兼容池。`IMemory` 只作为公开 `Release(IMemory)` / `MemoryPoolHandle.Acquire()` 的接口类型存在，不能作为动态 Type 路径的池类型准入条件。

## 泛型静态池与按需句柄缓存

不扫描程序集，不枚举所有 `MemoryObject` 类型，不建立全局类型索引表。

原因：常规路径已经天然是泛型静态池：

```text
MemoryPool.Acquire<T>()
-> MemoryPool<T>.Acquire()
-> MemoryPool<T> 的静态字段
```

`Acquire<T>` / `Release<T>` 不需要 `Type`，不需要反射，不需要全局查找表。

动态 Type 路径只处理当前传入的一个类型：

```text
GetHandle(Type type)
1. 读取 RuntimeTypeHandle。
2. 查询已 materialized 的句柄缓存。
3. 未命中时校验 type 继承 `MemoryObject`。
4. materialize `MemoryPool<T>`。
5. 缓存该 type 对应的 `MemoryPoolHandle`。
6. 返回句柄。
```

要求：

- 不执行 `Assembly.GetTypes()`。
- 不遍历所有 `MemoryObject` 子类。
- 不维护业务侧类型清单。
- 不为未使用类型创建池。
- `Acquire(Type)` 保留 public，但它是动态入口，不承诺等价于泛型热路径。
- 框架必须缓存 `MemoryPoolHandle`；重复 `Acquire(Type)` 只能做 O(1) 句柄查找，不能每帧重复走 `MakeGenericType` / `RunClassConstructor`。
- 调用方缓存 `MemoryPoolHandle` 只是可选优化，不属于业务常规流程。
- 句柄缓存可由 `RuntimeTypeHandle` 开放寻址表实现，避免 `Dictionary<Type, Handle>` 扩容进入 Runtime 热路径。
- 动态 Type 路径只接受 `MemoryObject` 子类；非 `MemoryObject` 即使实现 `IMemory` 也必须拒绝。
- 动态 Type 路径必须拒绝 `null`、非 class、abstract、open generic、没有 public parameterless constructor 的类型。
- 动态 Type 校验失败必须在 materialize `MemoryPool<T>` 前结束，开发期报明确异常，Release 模式拒绝或 no-op，不能退回旧 `IMemory` 池。

IL2CPP / AOT 边界：

```text
Acquire(Type) 只支持已被代码引用、link.xml 保留或框架构建流程保留的 `MemoryObject` 类型。
框架不通过扫描业务程序集生成池类型清单。
如果某个类型完全只通过运行时字符串 / 远程配置出现，IL2CPP 裁剪后不保证可 materialize。
```

保留信息必须来自框架构建流程、Unity link 保留规则或类型本身的正常代码引用，不能要求业务逐类型维护清单。
如果需要支持配置驱动的动态类型，必须由构建流程生成 AOT anchor / link 保留文件，显式引用对应 `MemoryPool<T>` 闭包泛型实例；仅保留业务类型本身不等价于保留池泛型实例。

## 分页池结构

当前单个 `T[] s_Stack` 扩容会触发 `new T[] + Array.Copy`。改为分页对象池：

```text
MemoryPool<T>
- PageHeader[] / NativeArray<PageHeader> / unsafe PageHeader* pageHeaders
- T[][] objectPages   // 每个 page 一个 T[]，只在 page 创建时分配
- SlotMeta[] / NativeArray<SlotMeta> / unsafe SlotMeta* slotMetas
- freePageQueue     // 只存 page handle，不存 slotId
- emptyPageQueue    // 只存 page handle，不存 slotId
- evictPageQueue    // 只存 page handle，不存 slotId
- PoolStats
- WatermarkState
- SchedulerState

PageHeader
- int ConstructedCount
- int FreeCount
- int LeasedCount
- int EmptyCount
- int FreeHead
- int EmptyHead
- int NextUninitializedSlot
- int PageGeneration
- int QueueGeneration
- int LastUsedFrame
- int Flags

SlotMeta
- int PageGeneration
- int SlotGeneration
- int Next
- byte State // Empty / Free / Leased / Releasing / Evicting / Evicted
```

slot 编码：

```text
slotId = (pageIndex << pageShift) | slotIndex
slotGeneration 独立存储在对象和 slot metadata 中
PageGeneration 用于 leased slot 归还校验
QueueGeneration 用于 page handle / scheduler queue 失效校验
```

效果：

- `Acquire` 从 `freePageQueue` 取 page，再从 page.FreeHead pop slot：O(1)。
- `Release` push 回 page.FreeHead，并按需把 page handle 放回 `freePageQueue`：O(1)。
- 扩容只新增 page，不拷贝旧数组。
- 淘汰可按 slot 或整页释放。
- 不会因一次峰值保留单个巨大 backing array。

slot 状态必须独立于对象状态：

```text
Empty  : page.Objects[slot] == null，slot 在 page.EmptyHead 链表，或 slotIndex >= NextUninitializedSlot
Free   : page.Objects[slot] != null，slot 在 page.FreeHead 链表
Leased : page.Objects[slot] == null，slot 不在任何 page 内空闲链表
Releasing: 对象正在执行 Clear()，禁止再次 Release / Acquire
Evicting : 对象正在执行 OnEvict()，禁止再次 Release / Acquire
```

Empty slot 计数不变量必须固定：

```text
EmptyCount = EmptyHead 链表节点数 + (pageSize - NextUninitializedSlot)
从 NextUninitializedSlot 取得隐式 Empty slot 时，必须先记录 slotIndex，再执行 NextUninitializedSlot++
从 EmptyHead 链表取得显式 Empty slot 时，不修改 NextUninitializedSlot
只要 EmptyCount > 0 且 page 非 Tombstone，该 page 必须能被 empty 调度发现
```

全局队列只保存 page handle：

```text
freePageQueue  : FreeCount > 0 且 page 非 Tombstone
emptyPageQueue : EmptyCount > 0 且 page 非 Tombstone
evictPageQueue : FreeCount > target 或 page 冷却完成
```

每个 page 通过 `InFreeQueue` / `InEmptyQueue` / `InEvictQueue` 状态位去重。同一个 page 在同一种队列内最多出现一次，禁止把 slotId 直接放进全局队列。

## Page / Handle 值拷贝与分配边界

Page 元数据不能设计成可变 class，也不能在修改路径中被 struct 值拷贝吞掉写入。

要求：

```text
PageHeader / SlotMeta / PoolStats / WatermarkState / SchedulerState 必须是 unmanaged struct 或等价的 native/unsafe 连续内存。
所有热路径修改必须通过 pageIndex / slotIndex 定位，并使用 ref local、unsafe pointer 或显式 read-modify-write 写回。
禁止 `var page = pages[pageIndex]; page.FreeCount--` 这种修改副本但不写回的写法。
禁止把 PageHeader / SlotMeta 放入 Queue/List 后再取出修改；全局队列只能保存 handle。
MemoryPoolPageHandle 必须是不可变值类型，只包含 poolId / pageIndex / QueueGeneration 等定位信息。
MemoryPoolHandle 允许值拷贝，但只能是不可变句柄副本；ActiveIndex、队列状态、统计等可变状态必须存放在池或 registry 内部，不能放在 handle 副本里。
```

托管分配边界：

```text
禁止为每个 page 分配 Page<T> class。
禁止热路径创建 List / Queue / Dictionary / lambda / delegate / closure。
T[] object page 和 SlotMeta 元数据块只允许在 page 创建、Boot / Loading 增长或明确记录的 miss 应急路径分配。
Acquire 命中和 ReleaseToFree 命中路径不得分配任何 class 或数组。
```

后续伪代码中的 `page.FreeHead` / `page.Objects[slot]` 只是为了阅读简洁，实际实现必须解释为通过 `pageIndex` 访问 `pageHeaders[pageIndex]` 和 `objectPages[pageIndex][slot]`，不能引入 `Page<T>` 托管对象。

状态迁移：

```text
CreateFree:
Empty -> Free
从 page.EmptyHead 或 NextUninitializedSlot 取 slot
new T()
初始化 OwnerHandle / PoolId / SlotId / PageGeneration / SlotGeneration / State = Free
page.Objects[slot] = item
slotMeta.PageGeneration = page.PageGeneration
slotMeta.State = Free
ConstructedCount++
EmptyCount--
FreeCount++
push page.FreeHead
page 入 freePageQueue

AcquireHit:
Free -> Leased
从 freePageQueue 取 page，从 page.FreeHead 取 slot
item = page.Objects[slot]
page.Objects[slot] = null
State = Leased
slotMeta.State = Leased
LeasedCount++
FreeCount--
如果 FreeCount > 0，page 重新入 freePageQueue

EmergencyCreateOne:
Empty -> Leased
从 page.EmptyHead 或 NextUninitializedSlot 取当前 slot
new T()
初始化 OwnerHandle / PoolId / SlotId / PageGeneration / SlotGeneration / State = Leased
slotMeta.PageGeneration = page.PageGeneration
slotMeta.State = Leased
ConstructedCount++
EmptyCount--
LeasedCount++
missDebt++

Release:
Leased -> Free
按 SlotMeta 校验 OwnerHandle / PoolId / SlotId / PageGeneration / SlotGeneration / State
State = Releasing
slotMeta.State = Releasing
memory.Clear()
State = Free
page.Objects[slot] = memory
slotMeta.State = Free
LeasedCount--
inUse--
FreeCount++
push page.FreeHead
page 入 freePageQueue

ReleaseOverHard:
Leased -> Empty
按 SlotMeta 校验 OwnerHandle / PoolId / SlotId / PageGeneration / SlotGeneration / State
State = Releasing
slotMeta.State = Releasing
memory.Clear()
State = Evicting
slotMeta.State = Evicting
如果实现 IPoolEvictable，调用 OnEvict() 一次
State = Evicted
OwnerHandle = default
PoolId = 0
PageGeneration = 0
SlotId = -1
PageGeneration = 0
SlotGeneration = 0
slotMeta.SlotGeneration++
slotMeta.State = Empty
LeasedCount--
inUse--
ConstructedCount--
EmptyCount++
push page.EmptyHead
page 入 emptyPageQueue

EvictFree:
Free -> Empty
State = Evicting
slotMeta.State = Evicting
如果实现 IPoolEvictable，调用 OnEvict() 一次
State = Evicted
page.Objects[slot] = null
OwnerHandle = default
PoolId = 0
PageGeneration = 0
SlotId = -1
PageGeneration = 0
SlotGeneration = 0
slotMeta.SlotGeneration++
slotMeta.State = Empty
ConstructedCount--
FreeCount--
EmptyCount++
push page.EmptyHead
page 入 emptyPageQueue
```

`Clear()` / `OnEvict()` 调用顺序固定：

```text
ReleaseToFree     : Clear() 一次，不调用 OnEvict()
ReleaseOverHard   : Clear() 一次，然后 OnEvict() 一次
EvictFree         : 不调用 Clear()，只调用 OnEvict() 一次
ReleaseTombstoneLeased: Clear() 一次，然后 OnEvict() 一次
```

`OnEvict()` 只表示对象离开池并释放额外资源；不能代替 `Clear()`，不能在同一个状态迁移里被调用多次。

`Clear()` / `OnEvict()` 额外约束：

```text
禁止重入当前 MemoryPool，包括 Release(this)、Acquire 同池对象、ClearAll 和触发 Scheduler。
禁止抛异常穿透 MemoryPool 状态迁移；开发期可捕获后报错并终止运行，Release 模式必须保证计数和链表不进入半迁移状态。
对象处于 Releasing / Evicting 时，任何 Release 都必须被判定为非法重入。
```

整页释放条件：

```text
LeasedCount == 0
FreeCount == 0
所有 slot 都是 Empty
page 不存在于 freePageQueue / emptyPageQueue / growthQueue / evictPageQueue
page.PageGeneration 未被任何 active slotId 引用
```

禁止释放仍有 `Leased` slot 的 page。禁止在 `Acquire` 后继续让 `page.Objects[slot]` 强引用借出对象。

释放整页：

```text
page.PageGeneration++
page.QueueGeneration++
丢弃 page.FreeHead / page.EmptyHead / SlotMetas / Objects
清除 InFreeQueue / InEmptyQueue / InEvictQueue 标志
page handle 可复用，但旧 slotId 必须因 PageGeneration 不匹配失效
旧队列 page handle 必须因 QueueGeneration 不匹配失效
```

禁止释放 page 后仍让旧 slotId 在任何全局队列中可用。全局队列不存 slotId，从结构上避免 O(n) 删除旧 slot。

## unsafe / Native 使用边界

当前对象本体仍是 managed class：

```csharp
T : MemoryObject, new()
```

因此不把对象本体塞进 `NativeArray<T>`。`NativeArray<T>` 更适合 unmanaged struct，不适合当前 class 对象池。

适合 native 化的是元数据：

```text
pageFreeHeads          NativeArray<int> / unsafe int*
pageEmptyHeads         NativeArray<int> / unsafe int*
pageNextUninitialized  NativeArray<int> / unsafe int*
pageQueueGenerations   NativeArray<int> / unsafe int*
pageFreeCounts         NativeArray<int> / unsafe int*
pageEmptyCounts        NativeArray<int> / unsafe int*
pageLeasedCounts       NativeArray<int> / unsafe int*
pageLastUsedFrames     NativeArray<int> / unsafe int*
slotMetas              NativeArray<SlotMeta> / unsafe SlotMeta*
freePageHandles        MemoryPoolPageHandle[] / UnsafeList<MemoryPoolPageHandle>
emptyPageHandles       MemoryPoolPageHandle[] / UnsafeList<MemoryPoolPageHandle>
activePoolHandles      MemoryPoolHandle[]   // managed array，除非 MemoryPoolHandle 是纯 unmanaged id handle
growthPoolHandles      MemoryPoolHandle[]   // managed array，除非 MemoryPoolHandle 是纯 unmanaged id handle
evictPageHandles       MemoryPoolPageHandle[] / UnsafeList<MemoryPoolPageHandle>
poolStats              NativeArray<PoolStats>
typeHandleCacheKeys    RuntimeTypeHandle[] / unsafe IntPtr*，native 路径优先存 RuntimeTypeHandle.Value
typeHandleCacheValues  MemoryPoolHandle[]   // managed array，不能放进 NativeArray，除非 handle 不含托管引用
```

对象本体仍在 managed `T[]` page 中。

Native 容器使用规则：

```text
NativeArray<T> / UnsafeList<T> 的 T 必须是 unmanaged，不能包含 object、string、T[]、delegate、Type、IMemory、MemoryObject 或任何托管引用。
PageHeader / SlotMeta / PoolStats / MemoryPoolPageHandle 必须保持 blittable / unmanaged；字段使用 int、uint、long、byte、IntPtr 等基础值。
如果 MemoryPoolHandle 内部持有 managed delegate、class handle 或 registry 引用，它只能放在 managed array 中，不能进入 NativeArray / UnsafeList / unsafe memory。
RuntimeTypeHandle 如果进入 unsafe/native cache，存 IntPtr key，不把 Type 对象放入 native 容器。
```

NativeArray 值拷贝规则：

```text
NativeArray<T> 本身是 struct，拷贝后共享同一块 native buffer；只能有一个明确 owner 负责 Dispose。
禁止把 NativeArray owner 按值长期保存到多个字段或闭包里，避免重复 Dispose 或 use-after-dispose。
NativeArray<T> indexer 返回值副本；修改元素字段必须 read-modify-write 写回，或通过 NativeArrayUnsafeUtility.GetUnsafePtr / unsafe pointer 获取可写地址。
禁止 `nativePages[i].FreeCount--` 这类依赖 ref indexer 的写法；实现必须显式写回或使用 pointer。
传递 NativeArray owner 给 helper 时优先使用 ref owner 或只传 unsafe pointer + length；helper 不拥有 Dispose 权限。
```

收益：

- 元数据连续，cache locality 更好。
- 调度队列无 GC。
- 避免 `List` / `Dictionary` 扩容。
- `Acquire` / `Release` 不依赖外部哈希表状态检查。

Native / unsafe 生命周期：

```text
Initialize:
- 只在框架初始化或非 Gameplay 阶段分配 Native 元数据块。
- Allocator 使用 Persistent。
- Native 容器 owner 集中保存在 registry / pool storage 中，不从属性返回可 Dispose 的副本。
- 初始化后必须记录 IsCreated / capacity / allocator / ownerGeneration。

GrowMetadata:
- Runtime Gameplay 命中热路径禁止扩容 Native 容器。
- 非 miss 的容量不足只记录 metadataGrowDebt，由 Scheduler 在允许阶段扩容。
- 但 `Acquire` 不能失败，因此必须预留 emergency metadata page / slot；Gameplay miss 可消耗预留槽完成当前对象创建。
- 如果 emergency metadata 也耗尽，设计必须二选一：允许一次明确计数的应急扩容，或引入可失败 API。当前 API 约束下选择“应急扩容 + 记录 metadataEmergencyGrowCount”。
- 应急扩容只允许扩出满足当前 miss 的最小 metadata 单元，禁止批量扩容和批量初始化。
- 扩容必须分配新 native block、复制有效范围、发布新指针/容器后再释放旧 block；发布过程中禁止 Scheduler 并发访问。

Dispose:
- MemoryPoolSetting.OnDestroy
- Domain reload
- Application quit
- ClearAllNativeMetadata
- Dispose 前检查 IsCreated；Dispose 后清空 owner、capacity、unsafe pointer，并递增 ownerGeneration。
- 禁止对 NativeArray / UnsafeList 结构体副本调用 Dispose。

LowMemory:
- 先停止增长。
- 再分帧 Evict Free slot。
- 最后释放全空 page 和空闲 native page。
```

所有 `UnsafeList` / `NativeArray` / unsafe 指针必须有集中 Dispose 路径。禁止只依赖 GC 或 Unity 进程退出回收 native memory。

## 对象状态替代 Dictionary 检查

`Dictionary<T, byte>` 不适合作为 Runtime 严格检查结构。推荐引入强约束基类：

```csharp
public abstract class MemoryObject : IMemory
{
    internal MemoryPoolHandle OwnerHandle;
    internal int PoolId;
    internal int SlotId;
    internal int PageGeneration;
    internal int SlotGeneration;
    internal byte State; // None / Free / Leased / Releasing / Evicting / Evicted

    public abstract void Clear();
}
```

泛型池约束升级为：

```csharp
where T : MemoryObject, new()
```

`Release` 常态 O(1) 检查：

```text
OwnerHandle 有效
PoolId 匹配 OwnerHandle
SlotId 解码出 pageIndex / slotIndex
pageIndex / slotIndex 边界合法
memory.PageGeneration == page.PageGeneration
slotMeta.PageGeneration == page.PageGeneration
slotMeta.SlotGeneration == memory.SlotGeneration
State == Leased
```

`Clear()` 约束：

```text
MemoryPool 自身 bookkeeping 保证 O(1) / 0GC
IMemory.Clear() 由业务类型实现，必须自行保证 Runtime 0GC
Release 的 O(1) / 0GC 承诺不包含 Clear() 内部逻辑
```

Runtime 下 `Clear()` 禁止字符串拼接、LINQ、临时 List/Dictionary、闭包、装箱、反射和任何非池化 class 分配。

`OnEvict()` 约束同 `Clear()`：

```text
MemoryPool 自身 Evict bookkeeping 保证 O(1) / 0GC
IPoolEvictable.OnEvict() 由业务类型实现，必须自行保证 Runtime 0GC
Evict 的 O(1) / 0GC 承诺不包含 OnEvict() 内部逻辑
```

解决：

- 未复用前的双重释放。
- 跨池释放。
- 非本池对象入池。
- `ClearAll` 后旧对象污染新池。
- 严格检查依赖 `Dictionary<T, byte>` 导致的 GC 和哈希开销。

不能承诺解决：

```text
对象释放后已经被重新 Acquire，旧持有者再次 Release 同一对象引用。
```

原因是对象引用本身没有携带租约 token；复用后的对象状态属于新租约，单靠对象内字段无法区分旧持有者。若必须检测这种误用，需要额外返回 lease token / handle，属于新的 API 设计。

## Acquire 算法

命中路径：

```text
Acquire<T>
1. 更新 acquire 统计。
2. freePageQueue 有 page：
   - pop page handle
   - 从 page.FreeHead pop slot
   - 校验 SlotMeta.PageGeneration / SlotGeneration / State
   - State = Leased
   - inUse++
   - 更新 page.LastUsedFrame
   - 如果 page.FreeCount 仍大于 0，page 重新入 freePageQueue
   - 返回对象
```

结果：

```text
MemoryPool bookkeeping O(1)
MemoryPool bookkeeping 0GC
```

池空路径：

```text
freePageQueue 为空：
1. EnsureEmergencySlot()
2. EmergencyCreateOne()
3. missCount++
4. missDebt++
5. 提高 targetLiveForecast / targetFreeReserve
6. 池加入 growthQueue
7. 返回对象
```

说明：

- 只创建当前必须返回的 1 个对象。
- 不在 `Acquire` 内批量补池。
- 不公开 `TryAcquire` 作为业务必需接口。
- 首次未知峰值无法保证 0GC，这是边界条件。

`EnsureEmergencySlot()`：

```text
1. 优先从 emptyPageQueue 取 page，再从 page.EmptyHead 或 page.NextUninitializedSlot 取 1 个 slot。
2. 如果没有可用 Empty page，创建 1 个新 Page。
3. 新 Page 初始化 EmptyCount = pageSize，NextUninitializedSlot = 0，FreeHead = -1，EmptyHead = -1。
4. 新 Page 只把当前 slot 绑定给 EmergencyCreateOne：slotIndex = NextUninitializedSlot，随后 NextUninitializedSlot++。
5. EmergencyCreateOne 统一执行 EmptyCount--。
6. 如果新 Page 仍满足 EmptyCount > 0，设置 page dirty-empty 标志；能写入 emptyPageQueue 则入队，队列满则记录 dirty page 身份或进入 cursor 修复。
7. 新 Page 剩余 slot 不在 Acquire miss 内批量入队；由 NextUninitializedSlot 懒暴露，或由 Scheduler 在预算内处理 dirty-empty page。
```

Gameplay 阶段允许 `EnsureEmergencySlot()` 做最小 page 分配，因为没有 `TryAcquire` 且 `Acquire` 不能失败；但 Acquire miss 只能创建 page 并绑定当前 slot，不能把整页剩余 slot 一次性加工进全局队列。必须记录 emergencyPageCreateCount，后续通过水位和分帧增长吸收。

## Release 算法

```text
Release<T>
1. null 直接返回或开发期报错。
2. 校验 OwnerHandle 有效。
3. 校验 PoolId 匹配 OwnerHandle。
4. 解码 SlotId 并校验 pageIndex / slotIndex 边界。
5. 校验 SlotMeta.PageGeneration / SlotGeneration / State。
6. 如果 page 为 Tombstone，执行 ReleaseTombstoneLeased。
7. 如果 freeCount < hardFreeReserveLimit，执行 ReleaseToFree。
8. 如果 freeCount >= hardFreeReserveLimit，执行 ReleaseOverHard。
9. 每个分支内部最多调用一次 memory.Clear()。
10. 每个成功释放分支必须且只能执行一次 inUse--。
11. 更新 release / lastUsed 统计。
12. 按需加入 evictQueue 或 dirtyQueue。
```

禁止在 `Release` 中执行：

```text
new T[]
Array.Copy
Compact
批量淘汰
Dictionary 检查
复杂水位计算
```

结果：

```text
MemoryPool bookkeeping O(1)
MemoryPool bookkeeping 0GC
不包含 memory.Clear() 内部逻辑
```

## Add / SetCapacity 新语义

### Add

```text
Add<T>(count)
Add(Type, count)
```

改为：

```text
提高 targetLiveForecast / targetFreeReserve / missDebt repayment target
当前阶段允许则立即小批量创建
Gameplay 阶段进入 growthQueue 分帧补
```

它是高级容量干预入口，只改变目标水位和偿还节奏；业务常规流程不依赖它。

### SetCapacity

```text
SetCapacity<T>(soft, hard)
```

改为：

```text
设置策略上下限
提高容量：只修改水位，实际增长由 Scheduler 分帧执行
降低容量：只降低目标，多余对象由延迟淘汰分帧释放
```

禁止在当前调用内大量 `new`、`Array.Copy`、`Compact`。

容量字段语义必须拆开：

```text
targetLiveForecast     : 预测近期 live 峰值，只包含 Leased 需求，用于优先级和水位学习
targetFreeReserve      : 期望空闲保留数量，只包含 Free
softFreeReserveLimit   : 常态 Free 保留上限
hardFreeReserveLimit   : 绝对 Free 保留上限
```

`hardCapacity` 不限制当前已借出的 live 对象数量。

原因：公开 API 不提供 `TryAcquire`，`Acquire` 也不能失败；因此 hard 不能是 live object 上限，只能是 Free 保留上限。超过 hard 的借出对象允许被创建，Release 后如果 Free 保留量已满，直接 Evict，不回到 Free 队列。

`Add` 不再保证调用返回后立即有 `count` 个可用对象。它只提高 `targetLiveForecast` / `targetFreeReserve` / `missDebt` 偿还目标；实际创建由当前阶段策略决定：

```text
Boot / Loading: 可同步小批量补足
Gameplay: 只入 growthQueue 分帧补足
LowMemory: 只记录目标，不主动补足
```

## 自适应水位算法

每个池维护：

```text
inUse
freeCount
constructedCount
createdCount
emergencyCreateCount
missCount
missDebt
lastAcquireFrame
lastReleaseFrame
lastMissFrame
acquireRateEwma
releaseRateEwma
burstEwma
fastPeakInUse
slowPeakInUse
targetLiveForecast
targetFreeReserve
```

目标 live 预测：

```text
targetLiveForecast =
min(
    max(
        fastPeakInUse + burstMargin,
        slowPeakInUse,
        acquireRateEwma * lookaheadFrames,
        missDebt * missBoost,
        minKeep
    ),
    softFreeReserveLimit
)
```

目标空闲保留量：

```text
targetFreeReserve =
min(
    max(
        burstEwma,
        acquireRateEwma * lookaheadFrames,
        missDebt * missBoost,
        minKeep
    ),
    softFreeReserveLimit,
    hardFreeReserveLimit
)
```

soft / hard 关系：

```text
softFreeReserveLimit 控制常态 Free 保留目标，水位算法默认不超过 soft。
hardFreeReserveLimit 控制绝对 Free 保留上限，Release 回池时不能超过 hard。
LowMemory 可临时把 softFreeReserveLimit / hardFreeReserveLimit 降到低于 live leased 数量；leased 不强制回收，只影响后续 Release 是否保留。
```

考虑因素：

- 上次使用时间。
- 使用频率。
- 最近峰值。
- 长期峰值。
- 突发强度。
- 创建数量。
- miss 次数。

这替代人工容量配置。

## 分帧增长

`Acquire` miss 后只创建当前对象，后续由调度器补债。

每帧：

```text
ProcessGrowth(growthBudget)
```

优先级：

```text
missDebt 高
最近发生 miss
acquireRateEwma 高
constructedCount < targetLiveForecast
freeCount < targetFreeReserve
burstEwma 高
```

创建 Free 对象的硬条件：

```text
freeCount < targetFreeReserve
freeCount < hardFreeReserveLimit
当前阶段允许创建
```

`targetLiveForecast` 只作为需求预测和优先级输入，不能单独触发创建 Free 对象；实际创建 Free 对象仍必须满足 `freeCount < targetFreeReserve` 和 `freeCount < hardFreeReserveLimit`。

增长步骤：

```text
1. 若 emptyPageQueue 为空，创建新 Page 或把已有 Page 的 NextUninitializedSlot 在预算内暴露为 Empty slot。
2. 从 emptyPageQueue 取 page，再从 page.EmptyHead 或 page.NextUninitializedSlot 取 slot。
3. new T()
4. 初始化 OwnerHandle / PoolId / SlotId / PageGeneration / SlotGeneration / State = Free。
5. push 到 page.FreeHead。
6. page 入 freePageQueue。
7. missDebt--。
```

增长预算按阶段变化：

```text
Boot / Loading: 高
Gameplay: 低
Background: 中
LowMemory: 关闭或极低
```

## 延迟淘汰

`Release` 不做销毁。由 Scheduler 分帧淘汰。

冷度：

```text
coldScore =
idleFrames * A
+ freeSurplus * B
- acquireRateEwma * C
- burstEwma * D
- missDebt * E
- recentMissPenalty
```

淘汰条件：

```text
freeCount > targetFreeReserve
missDebt == 0
idleFrames > graceFrames
coldScore > threshold
```

每帧淘汰：

```text
evictCount = min(
    freeCount - targetFreeReserve,
    perFrameEvictBudget
)
```

淘汰动作：

```text
1. 取冷 Free slot。
2. 执行 EvictFree 状态迁移。
3. 如果 page 满足整页释放条件，释放 Page。
```

## ClearAll

`ClearAll` 不再简单把 `inUse` 清零。

正确行为：

```text
清空 free 对象
无 Leased 的 page 立即释放或标记 empty
仍有 Leased 的 page 进入 Tombstone 状态，保留 page metadata
仍借出的 Tombstone page 对象 Release 时直接 Evict，不回池
重置水位统计或按策略保留慢速水位
```

Tombstone Page：

```text
page.Flags |= Tombstone
page.QueueGeneration++
page.PageGeneration 不递增；仍借出的旧对象必须能用旧 PageGeneration 完成 Tombstone release 校验
page.Objects 中的 Free 对象执行 TombstoneEvictFree 并清空，不入 emptyPageQueue
page.FreeHead = -1
page.EmptyHead = -1
page.NextUninitializedSlot = pageSize
page.FreeCount = 0
page.EmptyCount = pageSize - page.LeasedCount
page.ConstructedCount = page.LeasedCount
清除 InFreeQueue / InEmptyQueue / InEvictQueue 状态位
旧 free / empty / evict 队列里残留的 page handle 取出时必须因 QueueGeneration 或 Tombstone 标志被丢弃
page metadata 保留到 LeasedCount == 0
Tombstone page 不再参与 growth / free / evict 调度
```

ReleaseTombstoneLeased：

```text
Leased -> Empty
识别 page.Flags 包含 Tombstone
仍按 OwnerHandle / PoolId / SlotId / PageGeneration / SlotGeneration / State 校验 leased slot
不调用回池逻辑
State = Releasing
slotMeta.State = Releasing
memory.Clear() 一次
State = Evicting
slotMeta.State = Evicting
如果实现 IPoolEvictable，调用 OnEvict() 一次
State = Evicted
OwnerHandle = default
PoolId = 0
SlotId = -1
PageGeneration = 0
SlotGeneration = 0
slotMeta.SlotGeneration++
slotMeta.State = Empty
LeasedCount--
ConstructedCount--
EmptyCount++
inUse--
如果 page.Flags 包含 Tombstone 且 LeasedCount == 0，page.PageGeneration++，释放 page metadata
不 push page.FreeHead
不入 freePageQueue / emptyPageQueue
```

TombstoneEvictFree 与普通 EvictFree 的区别：

```text
Free -> Empty
调用 OnEvict() 一次
清空对象 OwnerHandle / PoolId / SlotId / PageGeneration / SlotGeneration
slotMeta.SlotGeneration++
slotMeta.State = Empty
ConstructedCount--
FreeCount--
EmptyCount++ 或在最后统一重算为 pageSize - LeasedCount
不 push page.EmptyHead
不入 emptyPageQueue
```

避免：

- 旧对象回到新池。
- `inUse` 统计失真。
- ClearAll 后双重状态污染。

## Release(IMemory) 优化

保留 public。

新路径：

```text
Release(IMemory memory)
1. cast MemoryObject
2. 如果不是 MemoryObject，拒绝释放
3. 检查 OwnerHandle.IsValid
4. 检查 PoolId 匹配 OwnerHandle
5. 解码 SlotId 并检查 pageIndex / slotIndex 边界
6. 检查 memory.PageGeneration / SlotMeta.PageGeneration / SlotGeneration 匹配
7. 检查 State == Leased
8. 调用 OwnerHandle.Release(memory)，由池内部根据 page Tombstone 状态决定 ReleaseToFree / ReleaseOverHard / ReleaseTombstoneLeased
```

Tombstone release 必须走对象内 OwnerHandle 指向的原池，不能进入 `GetType -> GetHandle(type)` 兜底。

`MemoryObject` 但 OwnerHandle 无效、PoolId 不匹配、SlotId 越界、memory.PageGeneration 不匹配、SlotMeta.PageGeneration 不匹配、SlotGeneration 不匹配或 State 非 Leased，开发期必须报错；Release 模式拒绝或 no-op，不能按 `memory.GetType() -> GetHandle(type)` 兜底。否则损坏对象会被错误塞进当前世代池，破坏双重释放和跨池释放检查。

这样保留 API，但对象热路径不靠 `GetType`，也不需要扫描全部池类型。

## Acquire(Type) 优化

保留 public。

新路径：

```text
Acquire(Type type)
1. handle = GetHandle(type)
2. handle.Acquire()
```

`GetHandle(type)` 只 materialize 当前 type。重复调用命中框架句柄缓存；调用方保存 `MemoryPoolHandle` 只是少一次缓存查询，不是正确性要求。

## Scheduler

不要每帧扫所有池。

使用队列：

```text
growthQueue
evictQueue
dirtyQueue
activeQueue
```

每个池有状态位：

```text
InGrowthQueue
InEvictQueue
DirtyStats
```

队列扩容规则：

```text
Acquire / Release 热路径：
- 只允许写入已有容量。
- 禁止 new array。
- 禁止 Array.Copy。
- 禁止 UnsafeList 自动扩容。

容量不足：
- 设置 QueueGrowDebt。
- 必须记录入队失败的 pool/page 身份，或保证后续 fixed cursor 能在预算内重新发现它。
- 当前池仍可继续完成本次 Acquire / Release。
- Scheduler 在 Boot / Loading / Background 阶段扩容队列。
```

队列必须去重。每个池通过状态位保证同一时刻最多存在于同一种队列一次，避免活跃池重复入队导致 O(n) 膨胀。

`QueueGrowDebt` 不能依赖已经满的业务队列来调度，否则会死锁。必须使用固定容量的全局 debt 标志：

```text
static bool HasQueueGrowDebt
static int QueueGrowDebtCount
static DirtyPoolRing dirtyPoolsFixed
static DirtyPageRing dirtyPagesFixed
```

`MemoryPoolSetting.Update` 或框架固定 tick 每帧先检查该标志；如果当前阶段允许扩容，则优先扩容 Scheduler 队列，再处理 growth / evict / dirty。

入队失败恢复规则：

```text
Release 已经把 slot 放回 page.FreeHead 但 freePageQueue 满：
  - 设置 page.HasFreeQueueDebt
  - 写入 dirtyPagesFixed；如果 dirtyPagesFixed 也满，则设置 pool.HasFreeScanDebt

EnsureEmergencySlot / EvictFree 产生 Empty slot 但 emptyPageQueue 满：
  - 设置 page.HasEmptyQueueDebt
  - 写入 dirtyPagesFixed；如果 dirtyPagesFixed 也满，则设置 pool.HasEmptyScanDebt

Scheduler 每帧必须在固定预算内先处理 dirtyPagesFixed，再处理带 scan debt 的 pool cursor。
发现 FreeCount > 0 && !InFreeQueue && !Tombstone 的 page 时重试入 freePageQueue。
发现 EmptyCount > 0 && !InEmptyQueue && !Tombstone 的 page 时重试入 emptyPageQueue。
```

每帧处理固定预算：

```text
ProcessGrowth(maxCreate)
ProcessEvict(maxEvict)
ProcessStats(maxPools)
```

复杂度：

```text
O(frameBudget)
```

不是：

```text
O(totalPoolCount)
```

线程模型：

```text
MemoryPool 默认 MainThread only。
Acquire / Release / Scheduler / ClearAll 不能跨线程并发调用。
page.FreeHead、page.EmptyHead、State、Page counters、Scheduler queues 都不做锁保护。
```

跨线程需求必须独立设计：

```text
Worker thread local pool
或
跨线程 release command queue，在主线程统一回池
```

禁止在当前设计里用 lock 把主线程池改成并发池；那会破坏热路径成本模型。

## 阶段策略

```csharp
public enum MemoryPoolPhase
{
    Boot,
    Loading,
    Gameplay,
    Background,
    LowMemory
}
```

策略：

```text
Boot / Loading:
- 增长预算高
- 允许补池
- 允许创建 page

Gameplay:
- Acquire 命中 bookkeeping 必须 O(1) / 0GC
- miss 只 emergency create 当前对象
- 后台小预算补池
- 不做重型 compact

Background:
- 处理增长和淘汰
- 回收冷池

LowMemory:
- 提高 coldScore
- 快速淘汰低频空闲页
```

## 最终承诺

能保证：

```text
业务不手动大量预热
公开方法名保留，高性能路径要求迁移到 MemoryObject
不依赖人工容量配置
命中路径 Acquire bookkeeping O(1)/0GC
Release bookkeeping O(1)/0GC，不包含 Clear / OnEvict
重复峰值自动学习吸收
冷池自动分帧释放
动态 Type API 保留但只支持 `MemoryObject`
严格检查不依赖 Dictionary
```

不能保证：

```text
首次未知峰值超出历史水位仍 0GC
```

原因：在不暴露失败接口、不手动配置容量、不提前知道峰值的前提下，首次超峰只能内部创建当前对象。

## 总结

MemoryPool 定位为自适应水位池：

```text
保留现有公开 API
业务自然 Acquire / Release
内部分页存对象
unsafe / native 存元数据
对象带 OwnerHandle / PoolId / SlotId / SlotGeneration / State
RuntimeTypeHandle 只做按需句柄缓存，不扫描全部类型
水位算法根据时间、频率、峰值、miss 自动调整目标容量
后台分帧增长和延迟淘汰
首次未知峰值允许最小应急创建
后续自动学习并吸收为 0GC 热路径
```

