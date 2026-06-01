# 资源绑定与租约重构设计

## 评审结论

本文档是评审后的设计稿。原始方案中的以下风险已经在本设计中修正：

- `ushort` 类型的 generation 在高频创建/释放场景下过于容易回绕。句柄统一使用 `uint Generation`。
- 句柄类型必须有真实的槽位承载。本设计使用 `LeaseSlot` 结构体，而不是租约类对象。
- `UnloadAsset(object)` 无法做到完全明确。它保留为旧接口兼容路径，并明确记录其歧义。
- 轮询 `Renderer.material` 可能创建 Unity 材质实例。运行时校验不能读取会实例化或分配内存的材质属性。
- `SubSprite` / `SubAssetsHandle` 必须纳入设计，因为当前它们是独立的资源生命周期路径。
- KeepAlive 初始设计应为每个资产一个缓存持有，而不是每次释放都无限增加计数。
- Destroy 不是唯一的拥有者释放点。对象池回收时也必须显式释放。
- Debugger 数据采集必须零分配；编辑器或玩家调试窗口的 UI 渲染可以在受控轮询间隔内分配。
- 所有返回资产对象的旧 API，包括异步和回调版本，都必须表示为 legacy direct ref。
- 绑定释放和转入 KeepAlive 必须是一次原子操作。否则资源可能在缓存持有记录写入前先进入 Idle。
- Owner 槽和 Binding 槽都需要 generation 校验，避免过期异步回调或过期 `OnDestroy` 释放被复用的 owner。
- 编辑器 Inspector 应沿用 ObjectPool 的可视风格，但 Resource Inspector 在大型项目中必须限制轮询频率。
- YooAsset handle 必须只有一个负责 Dispose 的拥有者；`AssetRecord` 和 `AssetObject` 不能同时拥有同一个 handle。
- 从未应用到组件上的过期加载结果默认进入 Idle，不应进入组件 KeepAlive。
- 材质绑定 API 必须显式区分 shared material 和 runtime material instance 语义。
- 资产 key 必须区分物理加载身份和绑定/视图身份。子精灵名称不应创建独立的父级 `SubAssetsHandle`。
- 替换绑定时，Unity 组件仍指向旧资产期间不能先释放旧 lease。要么先清空组件槽位，要么先应用新资产，再释放旧 lease。
- 绑定槽释放时必须清空所有托管 Unity 对象引用，因为已销毁的 Unity 对象仍可能被托管引用保活。
- Resource 调试面板应报告由 Resource 拥有的外部 handle lease，不能假装统计 `ResourceService` 之外创建的原始 YooAsset handle。
- KeepAlive 初始设计为每个资产一个缓存持有。只有指标证明必要时，后续才扩展为按策略的缓存持有。
- 目标设计中不保留 `AssetsReference` 作为兼容适配层。Prefab 源引用和材质引用直接迁移到 `ResourceOwner` + `ResourceBindingService`。
- `ResourceOwner` 必须是生命周期域组件，而不是给每个绑定目标都添加的组件。热路径 API 应传入 owner 上下文，避免反复向父节点查找。
- 移除 `AssetsReference` 时，Prefab 源绑定和材质源绑定必须在同一迁移阶段具备，否则材质引用会失去释放 owner。
- 运行时实现不能在预期的资源释放路径中使用 `try/catch` 或异常控制流。
- Owner 解析缓存、KeepAlive 缓存、资产记录和绑定记录不能重复表达所有权。每个缓存只承担一个明确职责，并只存 id，不额外存 Unity 对象引用。
- 严格 no-GC 模式要求提前挂载或预热 `ResourceOwner` 根节点，并预热槽页。运行时 `AddComponent` 只能作为非热路径 fallback。
- 生命周期域所有权要求显式 owner 上下文，或预先建立 target-to-owner 映射。稳定绑定路径中不能沿父链查找。
- 高级原始 handle 集成必须使用槽位支撑的值类型 lease，不能为每次加载创建 adapter 对象，也不能引入第二套所有权缓存。
- Debug snapshot struct 只用于编辑器/调试页记录。运行时所有权逻辑必须使用 id 和 `ref` 访问槽位，不能复制大结构体。
- 严格 no-GC 生产模式与非严格迁移/编辑器 fallback 模式是两套不同契约。可能分配内存的 fallback 代码必须在热绑定路径中禁用或通过配置排除。

本设计不是一个小补丁。它是为了明确所有权、降低稳定运行期 GC 而设计的可落地架构，但必须分阶段迁移。

## 目标

资源系统必须清楚地区分以下概念：

- 活跃使用：UI、Renderer、Prefab 实例、子精灵绑定或玩法代码正在使用资产。
- KeepAlive 缓存：资产短时间保留，用于避免重复加载抖动。
- Idle 存储：资产 handle 已无引用，但释放被 Idle 过期时间延迟。
- 底层池存储：仅为实现细节，不能作为资源所有权的事实来源。

`ObjectPool.SpawnCount` 不能作为资源所有权的事实来源。它可以继续作为内部存储实现细节存在，但 Resource Debugger 必须基于 Resource 记录解释所有权。

运行时热路径必须满足以下约束：

- 不使用 LINQ。
- 不使用 lambda 或 closure 回调。
- 不使用 `try/catch` 或异常驱动的控制流。
- 热路径中不对字典或集合使用 `foreach`。
- 不使用 `ToArray`、iterator/yield 状态机或集合拷贝。
- warmup 后，`SetSprite`、`SetSubSprite`、`SetMaterial`、Prefab 引用和绑定请求不能产生每次调用的类对象分配。
- 正常增长期间不能触发大块连续数组搬迁。
- 不能每帧全表扫描。
- 热路径不能复制大结构体。
- 不能轮询可能实例化或分配内存的 Unity API，例如 `Renderer.material`。

Unity 和 YooAsset 内部可能分配内存。框架拥有的运行时热路径在 warmup 后不能额外增加分配。首次 `AddComponent`、首次字典扩容、首次页分配只允许发生在非严格迁移/编辑器路径中，或显式 preload/warmup 阶段。

生产环境的严格 no-GC 模式不是“尽量不分配”。它要求玩法场景需要的所有 Resource 页、字典、owner、target 注册和 debugger buffer，都在热绑定开始前准备好。如果严格模式下容量不足，API 应返回失败状态或输出开发诊断，而不是静默分配。

严格 no-GC 模式要求更高：

```text
预先把 ResourceOwner 挂到 prefab/window/pool 根节点上。
在视图构建或对象池 spawn 阶段，把子 target id 注册到 owner。
按预期容量预热槽页和字典。
使用显式 owner 的绑定 API。
在热 UI 列表中禁用只有 target 的 owner 自动解析。
玩法运行期间不分配 debug snapshot buffer。
```

## 实现硬约束

以下约束适用于运行时服务和绑定 API：

```text
预期的 acquire、bind、release、keep-alive、expiry 路径中不使用 try/catch/finally。
不为每个请求创建类对象。
不为每次加载创建 handle adapter 类；高级 handle 使用槽位支撑的值类型 lease。
不使用 closure validator。
不使用 LINQ。
不使用 iterator block。
不使用 ToArray 或完整集合拷贝。
热路径中不使用会 resize 的大块连续数组。
除活跃 BindingSlot 记录外，不重复保存 UnityEngine.Object 引用。
cache warmup 后不重复做父链 owner 查找。
热路径中不通过运行时层级遍历发现生命周期根节点。
运行时采集阶段不拼接 key、owner name 或 slot label 字符串。
严格 no-GC 模式下，不允许会分配内存的 fallback owner 创建或 fallback 层级搜索。
debug 表采集不能复制超过调用方请求可见页的数据。
```

热路径中允许复制的值类型只应是很小的 id 和 handle，例如 `int`、`uint`、`long`、`ResourceLeaseHandle`。任何超过两个 primitive word 的结构体都应通过 `in` 传递，或通过槽位 `ref` 访问。Snapshot struct 不是热路径类型。

失败清理必须通过显式状态转换和 generation 校验表达，不能依赖大范围异常处理。编辑器工具在手动触发时可以分配内存，但运行时采集 API 必须保持零分配。

## 当前问题

当前系统同时存在多套生命周期模型：

- `SetSprite` 使用 `ResourceExtComponent` 和 `SetSpriteObject`。
- `SetSubSprite` 使用 `ResourceExtComponent.SubSprite`、`SubAssetsHandle` 和 `SubSpriteReference`。
- `SetMaterial` 使用 `AssetsSetHelper`、`AssetsReference` 和 `MaterialInstanceReference`。
- Prefab 实例化使用 `AssetsReference`。
- 直接 `LoadAsset` 的调用方依赖 `UnloadAsset(object asset)`。
- 原始 `AssetHandle` API 返回 YooAsset handle；一旦外部代码持有该 handle，`ResourceService` 就无法完整观测其生命周期。
- 公开回调 API 可以接收调用方创建的 lambda。框架无法阻止调用方分配 delegate；无 closure 规则只适用于框架拥有的热路径和新的绑定 API。

这些路径会导致清理语义不一致。当前 `SpriteKeepAliveExpireTime` 通过延迟 `UnloadAsset` 让资产保持存活，因此 Asset Pool 看起来仍被引用。它本质是缓存持有，但 Debugger 中容易被误读为 UI 泄漏。

`SetMaterial` 还有精度问题：同一个 `GameObject` 反复替换材质时，旧材质引用可能一直留在 `AssetsReference` 中，直到对象销毁。

重构后的生产所有权路径会移除 `AssetsReference`。如果同一个 prefab 或材质同时由 `AssetsReference` 和 `ResourceOwner` 管理，就会形成两套所有权模型，使泄漏诊断更困难。

## 架构

推荐架构如下：

```text
ResourceService
  拥有 AssetRecord、YooAsset handle、加载去重和引用计数。

ResourceBindingService
  拥有 target + slot -> asset 的绑定关系，包括 sprite、sub-sprite、material、prefab ref 等。

ResourceKeepAliveCache
  ResourceService 的内部子系统，拥有 TTL 缓存状态。KeepAlive 不是活跃使用。
  初始设计为每个 AssetSlot 一个缓存持有。只有后续 profiling 证明同一个 handle
  对 sprite、material、prefab source 需要不同保留策略时，才增加多个策略 bucket。

Resource Debugger / Inspector
  分别展示 Active、KeepAlive、Idle、Loading 和 Handle 状态。

ObjectPool
  迁移期间可以继续作为内部存储池存在，但不能作为所有权事实来源。
```

`ResourceExtComponent` 应缩减为很薄的扩展入口，或在调用方迁移到 `ResourceBindingService` 后删除。它不应长期保留为第二套生产所有权模型，也不应长期拥有 Sprite 专用缓存语义。

迁移期间，旧 extension method 可以转发到 `ResourceBindingService`，但不能维护并行缓存、观察者列表或资源引用列表。

YooAsset handle 只能由一层拥有并负责 dispose。当前 `AssetObject.Release()` 会 dispose handle。`AssetRecord` 成为 owner 后，剩余的 ObjectPool `AssetObject` 要么从资产生命周期中移除，要么转换为不拥有 handle 的池化存储项。它不能对 `AssetRecord` 拥有的 handle 调用 `AssetHandle.Dispose()`。

## 非目标

不要构建单独的 `SetSprite` 资源池。那只是把歧义换了位置。资源生命周期属于 `ResourceService`，组件赋值属于 `ResourceBindingService`。

第一步不要替换所有 ObjectPool 内部实现。第一次迁移应在保留现有加载行为的同时，增加可观测性和明确的 Resource 记录。

不要把 `UnloadAsset(object)` 变成推荐 API。当多个系统拿到同一个 `UnityEngine.Object` 时，它无法精确表达所有权。

不要长期保留 `AssetsReference` 作为兼容适配层。如果迁移需要临时的分支内 adapter，它不能作为第二套生产所有权路径发布。

## ResourceService 数据模型

使用值类型 handle 和分页槽数组。不要为每个 lease 分配一个类对象。

```csharp
public readonly struct ResourceLeaseHandle
{
    public readonly int Index;
    public readonly uint Generation;
}
```

`ResourceLeaseHandle.Index` 指向 `LeaseSlot`，不是直接指向 `AssetSlot`。

内部槽位布局：

```text
AssetSlot
  int LoadKeyId
  int AssetInstanceId
  UnityEngine.Object Asset
  ResourceOwnedHandle Handle
  uint Generation
  int DirectRefCount
  int LegacyDirectRefCount
  int BindingRefCount
  int KeepAliveRefCount
  int LoadingWaiterHead
  int NextByUnityObject
  int KeepAliveExpireTick
  int IdleExpireTick
  int ExpireQueuePrev
  int ExpireQueueNext
  int LastKeepAliveOwnerId
  long LastKeepAliveSlotKey
  byte AssetKind
  byte HandleKind
  byte State
  byte ExpireQueueKind

OwnerSlot
  int OwnerId
  int GameObjectId
  uint Generation
  int BindingHead
  int PendingRequestHead
  int RegisteredTargetHead
  byte State

LeaseSlot
  int AssetId
  int OwnerId
  long SlotKey
  uint Generation
  byte Kind
  byte State
  int NextByAsset

BindingSlot
  long SlotKey
  int OwnerId
  int TargetGameObjectId
  int TargetComponentId
  uint OwnerGeneration
  UnityEngine.Object Target
  UnityEngine.Object AppliedAsset
  UnityEngine.Object RuntimeObject
  int AssetId
  int ViewKeyId
  ResourceLeaseHandle Lease
  uint Version
  int NextByOwner
  ushort SubIndex
  byte SlotType
  byte Flags

RegisteredTargetSlot
  int TargetComponentId
  int OwnerId
  uint OwnerGeneration
  int NextByOwner
  byte State

LoadRequestSlot
  int AssetId
  int LoadKeyId
  int ViewKeyId
  int OwnerId
  uint OwnerGeneration
  int TargetComponentId
  int BindingIndex
  long SlotKey
  uint Version
  byte RequestKind
  byte State
  int Next
```

使用分页 slab：

```text
PageSize: 256 或 512
Index: pageIndex * PageSize + offset
空闲槽通过 int free list 链接。
热路径通过 ref 访问槽位。
```

分页 slab 用于频繁变化的热运行时记录：`LeaseSlot`、`BindingSlot`、`LoadRequestSlot` 和 `OwnerSlot`。如果 UI 或对象池启用了只有 target 的迁移 API，`RegisteredTargetSlot` 也属于这组分页记录。

只有在最大加载资产数量由配置约束，并且玩法前已预热时，`AssetSlot` 才可以先使用字典加索引存储。如果运行时资产数量可能增长，`AssetSlot` 也必须使用同样的分页增长模型。热路径中不能依赖会 resize 的大块连续 `AssetSlot[]`。

严格 no-GC 模式下，`RegisteredTargetSlot` 必须禁用或预热。严格模式中如果缺少注册，只有 target 的绑定应失败，而不是分配注册槽或在绑定时遍历层级。

`ResourceOwnedHandle` 是概念上的 owner 字段，不是每次加载分配的类对象。实现可以在内部存一个小的 struct union，或分开存类型化字段，但每个 `AssetSlot` 只能有一种有效 handle：

```text
AssetHandle          普通 LoadAsset handle。
SubAssetsHandle      LoadSubAssets / 图集父资源 handle。
ExternalHandleLease  Resource 为高级 handle API 包装的外部 handle lease。
```

不要在同一条记录中同时存储有效的 `AssetHandle` 和 `SubAssetsHandle`。这会让所有权不清楚，并可能在迁移期间造成重复 dispose。

如果高级 handle API 需要 Resource 所有权，应把外部 handle 状态存入槽位，并返回值类型 handle。运行时路径中不要为每个原始 handle 分配 adapter 类。不要让同一个原始 YooAsset handle 同时存放在外部调用方对象和拥有它的 `AssetSlot` 中。

`BindingSlot.Target`、`BindingSlot.AppliedAsset` 和 `BindingSlot.RuntimeObject` 是托管 Unity 对象引用。每次绑定释放、owner 释放、过期请求清理和服务关闭时都必须清空它们。Unity 已销毁对象会与 null 比较为相等，但如果仍存于槽位，会继续被托管引用保活。

`OwnerSlot.BindingHead` 和 `BindingSlot.NextByOwner` 允许 owner 释放时不扫描所有 binding。另一个查找表把 `OwnerId + SlotKey` 映射到 `BindingSlot`，用于替换绑定。这两条链接必须在同一条代码路径中保持同步。

避免重复缓存：

```text
AssetRecord / AssetSlot 拥有资产生命周期和 handle 状态。
BindingSlot 拥有活跃 target 引用。
Owner 解析缓存只拥有 TargetComponentId -> OwnerId / Generation id。
KeepAlive 缓存只拥有 TTL 状态和 AssetSlot 链接。
Inspector snapshot 只在玩法逻辑之外拥有拷贝出来的显示页结构体。
```

不要在 owner cache、keep-alive cache 和 binding cache 中同时存储同一个 Unity 资产引用。KeepAlive 应只通过 id 指向 `AssetSlot`。

`AssetInstanceId` 和 `NextByUnityObject` 用于支持旧的 `UnloadAsset(object)` 查找。这个查找会把一个 Unity object instance id 映射到一个或多个 `AssetSlot`，因为同一个 Unity 对象可能通过多个逻辑 key 访问。新代码不能依赖这条路径表达精确所有权。

## 资产状态语义

Resource 状态必须明确：

```text
Loading
  YooAsset 操作正在执行，且有请求在等待。

Active
  DirectRefCount + LegacyDirectRefCount + BindingRefCount > 0。

KeepAlive
  DirectRefCount == 0
  LegacyDirectRefCount == 0
  BindingRefCount == 0
  KeepAliveRefCount > 0。

Idle
  DirectRefCount == 0
  LegacyDirectRefCount == 0
  BindingRefCount == 0
  KeepAliveRefCount == 0
  Handle 有效
  正在等待 AssetExpireTime。

Released
  Handle 已 dispose。
```

当资产从 KeepAlive 被重新获取时，应先消费或取消 keep-alive 持有，再计入活跃使用。这样 Debugger 展示不会产生歧义：

```text
Active asset: DirectRef + LegacyDirectRef + BindingRef > 0, KeepAliveRef == 0
Cached asset: DirectRef == 0, LegacyDirectRef == 0, BindingRef == 0, KeepAliveRef > 0
```

状态转换必须通过一个统一函数完成，例如 `ReleaseLeaseAndMaybeKeepAlive`。不要在多个调用点分别实现“递减 ref、判断 idle、然后可能添加 keep-alive”。这种拆分可能导致同一个资产在缓存持有记录写入前先进入 idle wheel。

`SpriteKeepAliveExpireTime` 控制语义缓存生命周期。`AssetExpireTime` 控制 idle handle 生命周期。二者不能用同一个计数器表示。

## 资产 Key 表

使用 `AssetKeyTable`：

```text
LoadKey
  packageName
  location
  assetType
  assetKind

ViewKey
  loadKeyId
  slotType
  subAssetName or subAssetId when needed

AssetKeyTable
  maps LoadKey to int LoadKeyId
  maps ViewKey to int ViewKeyId
```

`LoadKey` 是 YooAsset 的物理加载身份。`ViewKey` 是绑定或子资源身份。不要把 `subAssetName` 放进父级 `LoadKey`，否则同一图集中的每个 sprite 都可能创建一个独立的父级 `SubAssetsHandle`。

示例：

```text
Sprite asset:
  LoadKey = DefaultPackage, Item_1000, Sprite, AssetHandle
  ViewKey = LoadKeyId, Image.sprite, none

Atlas sub-sprite:
  LoadKey = DefaultPackage, ItemAtlas, Sprite[], SubAssetsHandle
  ViewKey = LoadKeyId, SubSprite, Item_1000
```

key 查找前必须规范化 `assetType`。如果同一个资产可以作为 `UnityEngine.Object`、`Sprite` 或 `Texture2D` 加载，就可能为同一个 YooAsset handle 创建重复记录。解析器应优先使用 `AssetInfo.AssetType` 或显式 `ResourceAssetKind` 规范类型，只把请求的 generic type 作为验证元数据保留。

`ResourceKey` 应尽可能携带解析后的 id：

```text
ResourceKey
  int LoadKeyId
  int ViewKeyId
```

不要在每次绑定调用中携带大结构体或复制 path 字符串。字符串重载只解析一次，然后进入基于 id 的路径。

使用带 ordinal 字符串比较的自定义 comparer。不要依赖可能装箱或产生多余拷贝的默认 struct comparer。key 结构体应保持小型，只存 id 和字符串引用，不复制 path buffer。当 key 结构体超过两个 int 时，用 `in` 传递。

字符串 API 由于调用方传入字符串，无法避免字典 hashing。它们应避免字符串拼接，并在首次解析后缓存 `LoadKeyId` / `ViewKeyId`。热路径调用方可以使用预解析 key：

```csharp
ResourceKey key = ResourceKeys.Sprite("Item_1000");
image.SetSprite(key);
```

兼容重载保留：

```csharp
image.SetSprite("Item_1000");
```

## Lease 模型

新的显式所有权使用 `ResourceLeaseHandle`。

```text
AcquireDirect -> 创建 Direct LeaseSlot，并增加 DirectRefCount。
AcquireBinding -> 创建 Binding LeaseSlot，并增加 BindingRefCount。
Release(handle) -> 校验 Generation，精确释放一个 lease。
```

旧 direct ownership 单独计数：

```text
LoadAsset<T>(string) -> 返回非 null asset 时 LegacyDirectRefCount++。
LoadAssetAsync<T>(string) -> 以非 null asset 完成时 LegacyDirectRefCount++。
LoadAsset<T>(string, Action<T>) -> 在以非 null 结果调用 callback 前 LegacyDirectRefCount++。
LoadAssetAsync(..., LoadAssetCallbacks, userData) -> 在 success callback 前 LegacyDirectRefCount++。
LoadGameObject / LoadGameObjectAsync -> prefab source 使用 Binding 或 InstanceSource ownership，不使用 legacy direct ownership。
UnloadAsset(object) -> 将 asset 解析为一个 LoadKeyId，并递减一个 legacy direct ref。
```

`DirectRefCount` 只用于新的显式 `ResourceLeaseHandle` direct ownership。`LegacyDirectRefCount` 只用于旧的返回对象 API。二者分离可以避免旧 API 掩盖新 handle 模型中的泄漏。

旧的返回对象 API 无法在编译期强制显式释放。它们的兼容契约是：如果 API 返回或回调了非 null asset object，调用方就拥有一个 legacy direct ref，并必须调用 `UnloadAsset(asset)`。新调用点应改用 `ResourceLeaseHandle`。

如果一个 Unity 资产实例映射到多个 key，`UnloadAsset(object)` 就存在歧义。兼容行为应为：

```text
1. 如果只有一个 active legacy direct record 拥有该 asset，优先使用唯一 key。
2. 如果存在歧义，在 editor/development build 中记录 warning。
3. release-critical 路径中不要猜测；新代码必须使用 `ResourceLeaseHandle`。
```

原始 YooAsset handle API 保留为高级/旧接口：

```csharp
AssetHandle LoadAssetSyncHandle<T>(...);
AssetHandle LoadAssetAsyncHandle<T>(...);
```

这些 handle 由外部拥有，除非通过 Resource 拥有的 external handle lease 获取，否则不能完整纳入 Resource 所有权。长期应优先使用 `ResourceLeaseHandle` 或其他槽位支撑的值类型 handle。不要为了让原始 handle 可观测而引入每个 handle 一个 adapter 类。

公开 callback API 仍可能接收调用方创建的 delegate。重构后的框架必须避免自己创建 closure 分配，但无法保证任意调用方的 delegate 构造都是零分配。

## 绑定模型

所有资源到 Unity 对象的赋值都使用：

```text
OwnerId + SlotKey -> BindingSlot
```

`SlotKey` 必须无碰撞。不要用简单相加构造。可以使用位打包或小型 struct key：

```text
SlotKey = ((long)(uint)targetComponentId << 32)
        | ((long)slotType << 16)
        | materialOrSubIndex
```

如果未来某个 slot 需要超过 16 bit 的 sub-index 数据，应使用带自定义 comparer 的 struct key，而不是强行塞进 `long`。

支持的 slot：

```text
Image.sprite
Image.material
SpriteRenderer.sprite
SpriteRenderer runtime material instance slot
SpriteRenderer sharedMaterial slot
Renderer runtime material instance slot
Renderer.sharedMaterial slot
SubSprite from SubAssetsHandle
Prefab source reference
Future slots: AudioClip, Spine assets, VFX, TMP font/material
```

绑定查找应使用 `OwnerId + SlotKey`。`ViewKeyId` 描述绑定了什么，但不能成为绑定字典 key 的一部分。把同一 slot 重新绑定到不同资产时，应更新同一个现有 `BindingSlot`，不能创建并行绑定。

## SetSprite 与 SetSubSprite 流程

```text
1. 计算 OwnerId 和 SlotKey。
2. 解析已有 BindingSlot，或取一个已预热的空闲槽。
3. 递增 BindingSlot.Version。
4. 只通过 version 取消旧请求；不使用 closure。
5. 解析或创建 LoadKeyId 和 ViewKeyId。
6. 通过 LoadKeyId 请求资产。
7. 异步完成使用 static callback 和 request id。
8. 校验 target 仍存活，且 version 仍匹配。
9. 只暂存释放所需的旧小型 handle、id 和 Unity 对象引用。
10. 获取新的 Binding lease。
11. 通过类型化 writer 应用 Image.sprite 或 SpriteRenderer.sprite。
12. 存储新的 BindingSlot 状态。
13. 释放旧 Binding lease，并在策略允许时把旧资产转入 KeepAlive。
14. 旧引用不再活跃后，清空临时变量或旧 runtime storage 中的托管引用。
```

步骤 2 在严格 no-GC 模式下不能分配。如果没有预热好的 `BindingSlot`，应返回失败状态，而不是静默增长存储。

步骤 9 暂存的值只是 completion/apply 路径上的局部变量。不要把旧 lease 或旧 Unity 对象引用存进 `LoadRequestSlot`；旧绑定必须保持活跃，直到新资产已验证并准备应用。

暂存值只限于 `ResourceLeaseHandle`、primitive id 和 Unity 对象引用。绑定热路径中不要复制 snapshot struct、带字符串字段的 key struct、数组、字典或大型值记录。

如果类型化 writer 在赋值前可能失败，它必须在释放旧 lease 前返回状态。失败路径释放新获取的 lease，并保持旧绑定状态不变。不要依赖异常做回滚。

异步加载完成流程：

```text
1. 通过 request id 解析 request slot。
2. 校验 owner generation、binding version、target id 和 target 存活状态。
3. 验证通过后才获取新 lease。
4. 只有验证仍匹配时才应用并存储 binding 状态。
5. 通过显式状态转换精确释放一次旧 binding lease。
6. 清理并回收 request slot。
```

目标设计中，绑定请求在 apply 前不创建 `LeaseSlot`。request slot 只固定 id 和 version 数据，YooAsset 加载操作由 `AssetSlot` 拥有。这避免了隐藏的 request ref 与后续 Binding ref 重复。

显式 clear 或 owner release 时，必须先清空 Unity 组件槽位，再释放 lease：

```text
Image.sprite = null
SpriteRenderer.sprite = null
Image.material = null when this binding owns it
Renderer.sharedMaterial = null only for explicit shared-material binding clear
Destroy runtime material instances before clearing RuntimeObject
```

替换时，先获取并应用新资产，再释放旧 lease。这样可以避免某一帧中组件仍指向已经无 Resource ref 的资产。

原地更新绑定时，写入新状态后不要清空活跃 `BindingSlot`。应先把旧 lease/applied/runtime 引用暂存到小型局部变量，写入新状态，然后只释放并清理旧引用。

子精灵必须显式建模：

```text
SubAssetsHandle 是父级拥有资源 handle。
LoadKey 是 atlas location + canonical sub-assets type。
ViewKey 是 LoadKeyId + spriteName。
BindingSlot.AppliedAsset 是父级 SubAssetsHandle 返回的 Sprite。
释放绑定时释放父级 SubAssets lease，而不是独立 Sprite handle。
KeepAlive 缓存父级 SubAssetsHandle，子精灵查找数据通过 key 保留。
```

迁移后，这会替代 `SubSpriteReference` 和 `_subSpriteReferences`。

## SetMaterial 流程

`SetMaterial` 必须迁移到同一套绑定系统：

```text
Image.material
SpriteRenderer.sharedMaterial or material slot
Renderer.sharedMaterial slot
Renderer runtime material instance slot
```

重要 Unity 规则：

```text
不要把 Renderer.material 或 SpriteRenderer.material 作为通用读取/校验路径。
读取 Renderer.material 可能实例化材质并分配内存。
绑定 API 必须显式选择 shared-material 或 runtime-instance 语义。
```

runtime material instance 流程：

```text
1. Source Material 由 Binding lease 持有。
2. Runtime Material instance 存入 BindingSlot.RuntimeObject。
3. 通过目标 writer 路径赋值 runtime instance。
4. 替换时，先创建并应用新的 runtime instance，再销毁旧 runtime instance，并释放旧 source lease。
5. 当 binding 被 clear、owner 被 release、对象被销毁或回收时，销毁 runtime instance。
```

创建 runtime material instance 是 Unity 分配。严格 no-GC 生产路径必须从 owner 作用域的池中预创建/复用 runtime material instance，或避免 runtime-instance binding。如果绑定期间调用 `new Material(...)`，runtime-instance binding 就不能宣称 no-GC。Shared-material binding 在 Resource 页和 owner 注册都预热后，可以满足严格 no-GC。

如果增加 runtime material instance pool，它应由 `ResourceBindingService` 或专用材质实例子系统拥有。它只在 active 或 pooled 状态下存储 runtime object id 和拥有的 `Material` 引用。它不能变成第二套 asset-source 缓存，也不能在 binding 释放 source lease 后继续持有 source material lease。

shared material 流程：

```text
使用显式 shared-material slot 语义。
不要意外把 shared material binding 转换成 runtime instance。
```

材质 API 不能调用 `AssetsReference.Ref(material, gameObject)`。材质 source lease 存储在由 `ResourceBindingService` 拥有的 material binding slot 中。

从材质路径移除 `AssetsReference` 前，必须先具备 shared material binding。不要在替代路径能创建并释放 material source lease 之前删除 `AssetsReference.Ref(material, gameObject)`。

材质 slot 的自动校验应保持有限。外部代码如果绕过 `ResourceBindingService` 覆盖 renderer material，不保证能立即检测到。应使用显式绑定 API，或启用带 pooled buffer 的 opt-in debug validation。

## LoadGameObject 流程

`LoadGameObject` 和 `LoadGameObjectAsync` 必须停止使用 `AssetsReference.Instantiate`。

新流程：

```text
1. 解析 prefab LoadKeyId。
2. 通过 ResourceService 获取 prefab source lease。
3. 实例化 prefab source GameObject。
4. 在实例根节点上添加或复用 ResourceOwner。
5. 在 owner 上注册 PrefabSource binding slot。
6. 把 prefab source lease 存入该 binding slot。
7. 在 ResourceOwner.OnDestroy、对象池回收或显式 ReleaseOwner 时，释放 prefab source lease。
```

失败处理：

```text
如果获取 prefab source lease 后 Instantiate 失败：
  立即释放 prefab source lease。

如果实例化后 ResourceOwner 创建或 PrefabSource binding 注册失败：
  若所有权尚未返回给调用方，则销毁已实例化 GameObject；
  立即释放 prefab source lease；
  不留下部分注册的 owner slot。
```

实例化出来的 GameObject 不直接拥有 YooAsset handle。它通过 `ResourceOwner` 拥有 prefab source binding。这样 prefab 引用能和 sprite、material、sub-sprite 一样显示在同一组 debugger 表中。

完成迁移后，`LoadGameObject`、`LoadGameObjectAsync`、`GameObjectPool` 或 prefab helper API 都不应再添加 `AssetsReference` 组件。

## Target 所有权

`ResourceOwner` 是生命周期域组件，不是每个绑定一个的组件。不要为每个 `Image`、`Renderer` 或材质槽添加一个 owner。

合法 owner 根：

```text
UI window root
RecyclerView / ViewHolder item root
Prefab instance root
GameObjectPool instance root
Explicit gameplay lifetime root
```

无效默认行为：

```text
Every Image gets ResourceOwner
Every Renderer gets ResourceOwner
Every SetSprite call adds a MonoBehaviour
Every material slot adds a MonoBehaviour
```

Owner 组件：

```text
ResourceOwner
  int OwnerId
  int GameObjectId
  uint Generation
  bool IsRegistered

OnDestroy()
  ResourceBindingService.ReleaseOwner(OwnerId, Generation)
```

这会直接替代：

- `AssetsReference`
- `MaterialInstanceReference`
- `SubSpriteReference`
- Sprite 专用 destroy observer

这个替代关注的是资源所有权。后续可以增加 opt-in 的 target destroy observer，但它只能作为独立可销毁 child 的存活信号，不能存储 lease、asset、owner 引用或 cache 数据。

`AddComponent<ResourceOwner>` 会在每个生命周期根上分配一次。它对窗口根、prefab 根和池化 item 根是可以接受的，但不能作为每次绑定的热路径分配。

绑定 API 应优先使用显式 owner 上下文：

```csharp
ResourceBindStatus BindSprite(ResourceOwner owner, Image image, ResourceKey key, ResourceBindingOptions options);
ResourceBindStatus BindSharedMaterial(ResourceOwner owner, Renderer renderer, ResourceKey key, ResourceBindingOptions options);
```

生命周期域注册应发生在热绑定之外：

```text
UI window build:
  在 window root 上创建/注册 ResourceOwner
  将该 owner 下已知 bind target 一次性注册

RecyclerView item create/reuse:
  在 item root 上创建/注册 ResourceOwner
  将 item child Image/Renderer id 一次性注册

Prefab/GameObjectPool spawn:
  在 instance root 上创建/注册 ResourceOwner
  如果需要 target-only API，一次性注册已知 child target
```

注册映射只存 id：

```text
TargetComponentId -> OwnerId, OwnerGeneration
```

它在受控的构建/回收路径中填充，而不是通过反复运行时层级搜索填充。内部使用从 `OwnerSlot.RegisteredTargetHead` 链接出来的 `RegisteredTargetSlot` 记录，这样 `ReleaseOwner` 和对象池回收可以按 owner 清理注册，而不必扫描所有 registered target。

兼容重载可以自动解析 owner，但只允许在首次绑定或 cache miss 时执行：

```text
1. 使用 options/context 中提供的 owner。
2. 使用目标组件 id 缓存的 owner。
3. 使用预构建 target-owner 注册映射。
4. development/editor fallback 可以沿父链搜索一次，并缓存结果。
5. 如果没有 owner，只在配置的生命周期根上创建 owner。
6. 如果无法识别生命周期根，返回失败状态，并在 development build 中记录日志，或要求显式 owner。
```

步骤 4 和步骤 5 属于非严格迁移/编辑器行为。严格 no-GC 模式下必须禁用，并且生产热 UI 绑定中应通过编译或配置排除。严格模式下，缺失 owner cache 或 target 注册会返回 `ResourceBindStatus.MissingOwner`，不会尝试层级搜索或 `AddComponent`。

不要每次 bind 都调用 `GetComponentInParent<ResourceOwner>()`。父链遍历只允许作为开发/迁移 fallback。热 UI 列表、池化 item 绑定和严格 no-GC 模式都应禁用它。

Owner 解析缓存必须 generation-safe：

```text
TargetComponentId -> OwnerId, OwnerGeneration
```

复用缓存 owner 前，必须验证缓存 owner slot 仍存活，且 generation 匹配。当 target 被显式注销、`ReleaseOwner` 执行、owner generation 变化，或 target observer 报告销毁时，应清除 target-owner cache entry。Unity instance id 在销毁后可能被复用，因此如果没有 owner generation 校验，单独使用 target component id 不是安全的长期 key。

不要因为某个 slot binding 释放了，就移除该组件的 target-owner 注册。同一个目标组件可以拥有多个 slot，例如 `Image.sprite` 和 `Image.material`。只有 target 注销、owner 释放、target observer 报告销毁，或 owner generation 变化时，才清除 target-owner 注册。

Owner 解析缓存不能存储 `GameObject`、`Component` 或 `ResourceOwner` 引用。它只存 id。活跃 `BindingSlot` 是唯一允许持有 target Unity 对象引用的运行时绑定记录。

清理 owner 注册时，应遍历该 owner 的 registered-target 链表，而不是扫描全局 target-owner 字典。

如果 child `GameObject` 可能在 owner root 仍存活时独立销毁，应为该 child 使用 opt-in 的轻量 target observer。不要默认给所有 child 添加 target observer。严格 no-GC 模式下，这些 observer 必须预先挂载，或在 pool/window 构建阶段创建，不能在 bind 时创建。

Destroy 不是唯一释放触发点。池化对象可能在 inactive 状态下仍存活。任何对象池或 UI recycle 路径都必须调用：

```csharp
ResourceBindingService.ReleaseOwner(ownerId, generation);
```

`ReleaseOwner` 不只是计数操作。它必须：

```text
1. 校验 owner generation。
2. 通过 version/generation 取消 pending request。
3. 清空该 binding 拥有的每个已知组件 slot。
4. 销毁 binding 拥有的 runtime object。
5. 释放 lease，并应用 keep-alive 策略。
6. 清空 BindingSlot.Target、AppliedAsset、RuntimeObject 和 owner link。
7. 通过 OwnerSlot.RegisteredTargetHead 清理 registered target slot。
```

如果一个 `GameObject` 被对象池复用，它的 `ResourceOwner` 必须获得新的 generation，或重新绑定到新的 owner slot。过期异步完成、过期 `OnDestroy` 和过期 recycle callback 在释放或应用任何内容前，都必须比较 owner generation。

对象池复用时必须重建或验证该实例的 target-owner 注册映射。不要假设 child component instance id 在销毁/重建流程中仍然有效。

`OnDisable` 不能作为通用释放触发点，因为它会在临时 deactivate 流程和 Unity 销毁级联期间触发。

如果组件被销毁但其 `GameObject` 仍存活，`ResourceOwner.OnDestroy` 检测不到。使用有限校验：

```text
Sprite slots:
  可以安全比较 Image.sprite / SpriteRenderer.sprite 与 AppliedAsset。

Material slots:
  不要轮询 Renderer.material。
  Shared material validation 是 opt-in，且必须使用非分配 API 或 pooled buffer。

General validation:
  使用 cursor 和 frame budget。
  不要每帧扫描所有 bindings。
```

## 加载请求模型

避免基于 closure 的取消。使用带版本的 request slot：

```text
Set request:
  BindingSlot.Version++
  LoadRequestSlot stores OwnerId, OwnerGeneration, BindingIndex, LoadKeyId, ViewKeyId, and Version

Load completion:
  Static callback receives request id
  Resolve LoadRequestSlot
  Check OwnerSlot.Generation == request.OwnerGeneration
  Check BindingSlot.OwnerGeneration == request.OwnerGeneration
  Check BindingSlot.Version == request.Version
  Check BindingSlot.TargetComponentId == request.TargetComponentId
  Check Target != null
  Apply or cleanup request slot
```

如果过期异步加载完成：

```text
1. 不应用到 target。
2. 不创建 Binding lease。
3. 清理并回收 request slot。
4. 如果没有其他 active ref，让 AssetSlot 默认转入 Idle。
5. 只有请求明确选择 load-result cache 时才进入 KeepAlive，不能进入 component keep-alive。
```

Component keep-alive 用于曾经真正应用到组件、随后被替换或释放的资源。从未应用的过期加载结果不是组件缓存命中，默认不应刷新 `SpriteKeepAliveExpireTime`。

如果未来实现增加显式临时 request lease，每条失败路径都必须精确释放一次，且它必须与 `BindingRefCount` 分开计数。推荐的第一版实现不增加这个 lease。

```text
Target destroyed
Owner generation mismatch
Binding version mismatch
Target component id mismatch
Asset load failure
Apply failure
Service shutdown
```

这样可以避免：

- `Func<bool>` validation closure。
- 回调中捕获 `Image` 或 `Renderer` 引用。
- warmup 后为每个 request 创建类对象。

服务由 Unity 主线程拥有。异步完成在接触 Unity 对象或 Resource 槽位前，必须继续回到 Unity 主线程。除非未来 loader 明确跨线程，否则不要在热路径中加锁。

## KeepAlive 缓存

KeepAlive 是缓存状态，不是活跃使用。

第一版实现中，每个 `AssetSlot` 只使用一个 keep-alive 持有。重复释放只刷新过期时间，不无限增加计数。如果后续 profiling 证明同一个 loaded handle 需要多个独立缓存策略，再把 key 扩展为 `AssetSlot + CachePolicyId`；不要提前实现这层复杂度。

KeepAlive 记录只存 `AssetSlot` id、过期 tick，以及可选的 last owner/slot id。它不能存 `UnityEngine.Object` 资产引用。`AssetSlot` 是唯一拥有 loaded asset object 引用的地方。

```text
On binding release:
  if active refs are zero and policy allows:
    在释放 binding lease 的同一次状态转换中创建或刷新 keep-alive hold
    将 asset 入队到 keep-alive wheel

On active reacquire:
  取消/消费 keep-alive hold
  从 keep-alive wheel 移除

On keep-alive expiry:
  释放 keep-alive hold
  if all refs are zero:
    将 asset 入队到 idle wheel
```

使用固定 bucket 的 timing wheel：

```text
TimingWheel
  BucketCount: 256 or 512
  TickSeconds: 1
  在 AssetSlot 中存绝对 expire tick。
  如果 bucket entry 尚未到期，则重新入队。
  每帧最多处理配置数量的 expired entry。
```

`TickSeconds = 1` 表示过期精度约为一秒。资源缓存过期可以接受这个精度，并能避免每帧 heap 或 sort 工作。

每个 `AssetSlot` 同时只能位于一个 expiry queue 中。`ExpireQueueKind` 记录当前链接属于 KeepAlive 还是 Idle。如果未来需要同时处于多个队列，应为每个队列增加独立 link 字段，而不是复用同一组 link。

刷新 keep-alive 时必须更新绝对 expire tick，并在必要时移动 entry。重新链接前必须先解除旧 bucket entry，否则同一 asset 可能在 wheel 中出现多次。

当很多 entry 落入同一个 bucket 时，wheel 不能在一帧内释放全部 entry。使用每帧处理上限，并在下一 tick 从同一个 bucket 继续。这里用过期时间精度换取帧时间稳定。

不要使用：

- `List.Sort`
- LINQ
- 每次过期一个 heap node
- 每帧全表扫描

低内存处理：

```text
先释放没有 active ref 的 KeepAlive entry。
然后释放 Idle entry。
绝不释放 active Direct 或 Binding ref。
```

## 公开 API 方向

临时保留旧 API：

```csharp
T LoadAsset<T>(string location, string packageName = "");
UniTask<T> LoadAssetAsync<T>(string location, CancellationToken token = default, string packageName = "");
void UnloadAsset(object asset);
```

新的推荐 direct ownership API：

```csharp
ResourceLeaseHandle AcquireDirect(ResourceKey key);
void Release(ResourceLeaseHandle handle);
```

Direct ownership API 也可以提供 `TryAcquireDirect(ResourceKey key, out ResourceLeaseHandle handle)`，用于预期失败路径。正常的资源缺失或 key 无效流程不要依赖抛异常。

UI 列表和热路径优先使用显式 owner 的绑定 API：

```csharp
ResourceBindStatus BindSprite(ResourceOwner owner, Image image, ResourceKey key, ResourceBindingOptions options);
ResourceBindStatus BindSubSprite(ResourceOwner owner, Image image, ResourceKey atlasKey, string spriteName, ResourceBindingOptions options);
ResourceBindStatus BindSharedMaterial(ResourceOwner owner, Renderer renderer, ResourceKey key, ResourceBindingOptions options);
ResourceBindStatus BindMaterialInstance(ResourceOwner owner, Renderer renderer, ResourceKey key, ResourceBindingOptions options);
ResourceBindStatus ReleaseBindings(ResourceOwner owner);
```

只有 target 的重载是迁移辅助 API，不是热路径推荐 API：

```csharp
ResourceBindStatus BindSprite(Image image, ResourceKey key, ResourceBindingOptions options);
ResourceBindStatus BindSubSprite(Image image, ResourceKey atlasKey, string spriteName, ResourceBindingOptions options);
ResourceBindStatus BindSharedMaterial(Renderer renderer, ResourceKey key, ResourceBindingOptions options);
ResourceBindStatus BindMaterialInstance(Renderer renderer, ResourceKey key, ResourceBindingOptions options);
ResourceBindStatus ReleaseBindings(GameObject owner);
```

字符串重载可以继续作为兼容 helper，但应解析为 `ResourceKey`。只有 target 的重载可以在迁移期存在，但必须缓存 owner 解析结果，不能每次调用都向父节点查找。

绑定 API 应返回小型 enum status，而不是对缺失 owner、无效 key、过期 owner generation、target 已销毁或服务关闭等预期失败抛异常。开发日志可以接受，异常驱动控制流不可以。

Handle API 让所有权显式化，避免通过 `UnityEngine.Object` 猜测。

避免模糊的 `BindMaterial(Renderer, ...)` 默认语义。调用方必须显式选择 shared material 或 runtime material instance。这样可以防止意外实例化材质，并让释放行为确定。

## Inspector 与 Debugger 设计

编辑器/运行时 UI 应遵循 `ObjectPoolComponentInspector` 风格：

- 只在运行时展示。
- 紧凑 toolbar。
- label/value 形式的 summary row。
- 带 badge、title 和右对齐 summary 的 foldout list row。
- 复用 snapshot buffer。
- 在安全位置提供手动 action。
- 支持 CSV export。
- 采集运行时数据时不使用 LINQ。
- 编辑器 Inspector 在 play 模式下限制 repaint 频率。

Debugger 采集必须零分配。编辑器/debug window 渲染 UI 文本可以分配，但应受轮询间隔限制，并且不能作为玩法逻辑执行。大型资源表中不要每次 inspector pass 都调用 `Repaint()`；使用可配置轮询间隔，例如 0.25s 或 0.5s。

运行时 snapshot 采集不能分配，也不能拷贝整张表：

```text
调用方拥有 buffer。
采集过程最多填充到 buffer length。
采集返回所需总数量。
分页使用 start index + max count。
排序只在编辑器中进行，且只操作可见页 buffer。
不使用 ToArray。
不构造 List<T>。
采集时不构建字符串。
不创建隐藏的整表临时 buffer。
```

大型项目需要增加：

- 按 package、location、state、owner 和 slot 搜索/过滤。
- 每页最大显示行数。
- 排序只作用于可见 debug page buffer，不排序内部运行时存储，也不复制整表。
- 缓存 owner 显示名。不要每次 repaint 都重建完整 GameObject path。
- Snapshot 分页或最大行数限制。存在数千条记录时，不要每次 repaint 都复制所有 binding。

### ResourceService Inspector

目标组件：

```text
ResourceComponent or ResourceService debug bridge
```

面板标题：

```text
Resource Service
```

Summary rows：

```text
Asset Record Count
Active Count
KeepAlive Count
Idle Count
Loading Count
Handle Count
Direct Ref Total
Legacy Direct Ref Total
Binding Ref Total
KeepAlive Ref Total
External Handle Lease Count
```

`Handle Count` 表示 `AssetRecord` / `AssetSlot` 中 Resource 拥有的有效 handle 数量，包括 `AssetHandle`、`SubAssetsHandle` 和 Resource 拥有的 external handle lease。

`External Handle Lease Count` 只统计通过 ResourceService lease API 获取的高级 handle。`ResourceService` 外部创建的原始 YooAsset handle 无法观测，不能显示成可靠的 Resource 所有权。

Foldout header 风格：

```text
[ASSET] package/location
Right summary: Active 0 | Bind 0 | Keep 1 | Idle 0
```

展开后的资产行：

```text
Load Key Id
Package
Location
Type
Kind
State
Direct Ref
Legacy Direct Ref
Binding Ref
KeepAlive Ref
KeepAlive Expire In
Idle Expire In
Handle Valid
Last Use Time
```

Owner 表列：

```text
Kind        Width 72   Direct / Legacy / Binding / KeepAlive / External
Owner       Width 180  Cached owner name or owner id
Slot        Width 148  Image.sprite / Renderer.material / SubSprite
TargetId    Width 72
Version     Width 64
Expire      Width 84
```

Actions：

```text
Release Idle
Release KeepAlive With No Active Refs
Release All Unused
Export CSV
```

Inspector action 绝不能释放 active Direct 或 Binding ref。会修改 live owner 的 action 必须先清空组件 slot，或隐藏在明确的危险调试按钮之后。

### ResourceBindingService Inspector

面板标题：

```text
Resource Binding Service
```

Summary rows：

```text
Owner Count
Binding Count
Pending Request Count
Sprite Binding Count
SubSprite Binding Count
Material Binding Count
Prefab Source Ref Count
Runtime Material Instance Count
Validation Cursor
Recycle Release Count
```

Foldout header 风格：

```text
[OWNER] UIShopWindow/GoodsItem_0
Right summary: Bind 3 | Pending 0 | Runtime 1
```

展开后的 owner 行：

```text
GameObject Id
Destroyed
Pooled Or Recycled
Binding Count
Pending Request Count
Last Release Time
```

Binding 表列：

```text
Slot        Width 156  Image.sprite / Renderer.sharedMaterial
Asset       Width 180  package/location or sub-sprite key
State       Width 72   Active / Pending / Released
Version     Width 64
Runtime     Width 84   None / Material
Last Use    Width 84
```

Pending request 表列：

```text
Request     Width 72
Slot        Width 156
Asset       Width 180
Version     Width 64
Status      Width 72
Elapsed     Width 84
```

Actions：

```text
Validate Now
Clear And Release Owner Bindings
Export CSV
```

`Clear And Release Owner Bindings` 必须先清空已知组件 slot，再释放 lease。只释放不清空会让 Unity 组件继续指向 Resource 已不再拥有 handle 的资产。

### 运行时 Debugger 窗口

添加类似 `ObjectPoolInformationWindow` 的运行时 debugger 页面：

```text
Resource Overview
Resource Assets
Resource Bindings
Resource KeepAlive
Resource External Handles
```

运行时 debugger 使用调用方拥有的 snapshot 数组：

```text
ResourceAssetInfo[] assetInfos
ResourceBindingInfo[] bindingInfos
ResourceOwnerInfo[] ownerInfos
ResourceKeepAliveInfo[] keepAliveInfos
```

这些数组由 debugger window 或 inspector bridge 持有并复用。不要在 `ResourceService` 或 `ResourceBindingService` 内部分配它们。

必需查询 API：

```csharp
int GetAssetInfos(ResourceAssetInfo[] results, int startIndex, int maxCount);
int GetBindingInfos(ResourceBindingInfo[] results, int startIndex, int maxCount);
int GetOwnerInfos(ResourceOwnerInfo[] results, int startIndex, int maxCount);
int GetKeepAliveInfos(ResourceKeepAliveInfo[] results, int startIndex, int maxCount);
```

即使 buffer 太小，每个 API 也返回所需总数量。采集只填充调用方提供的容量和请求页。`maxCount` 必须 clamp 到 `results.Length`。

### CSV Export

CSV export 遵循 `ObjectPoolComponentInspector`：

```text
Resource Assets:
LoadKeyId,Package,Location,Type,Kind,State,DirectRef,LegacyDirectRef,BindingRef,KeepAliveRef,KeepAliveExpireIn,IdleExpireIn,HandleValid,LastUse

Resource Bindings:
Owner,TargetId,Slot,LoadKeyId,ViewKeyId,Asset,State,Version,RuntimeObject,LastUse

Resource KeepAlive:
LoadKeyId,Asset,Owner,Slot,ExpireIn,Policy
```

导出是编辑器-only 且由用户触发，因此可以分配内存。

CSV export 不能在玩法运行时 UI 中直接调用，除非有明确的 debug/editor gate。字符串和临时 buffer 分配只能发生在该 gate 内。

## 运行时 Snapshot Struct

只为 debugger 页面使用小型 snapshot struct。它们可以包含缓存的字符串引用，但采集过程不能构建新字符串。

Snapshot struct 应通过数组 index 或目标数组元素的 `ref` 填充。不要从逐记录查询方法中按值返回大结构体。避免嵌套会复制字符串或大值字段的结构体。

运行时玩法代码不能消费这些 snapshot struct。Resource 和 binding 逻辑必须通过 id 和 `ref` 访问内部槽位。复制出来的 snapshot 页只是调试展示边界，不是所有权模型。

```csharp
public struct ResourceAssetInfo
{
    public int LoadKeyId;
    public string Package;
    public string Location;
    public string TypeName;
    public ResourceAssetKind Kind;
    public ResourceAssetState State;
    public int DirectRefCount;
    public int LegacyDirectRefCount;
    public int BindingRefCount;
    public int KeepAliveRefCount;
    public float KeepAliveExpireIn;
    public float IdleExpireIn;
    public bool HandleValid;
    public float LastUseTime;
}
```

不要直接暴露内部槽数组。

## 迁移计划

### Phase 1：可观测性

- 在保持现有行为的同时增加 `AssetRecord` 计数。
- 增加 ResourceService inspector/debugger snapshot。
- 分别显示 Active、KeepAlive、Idle、Loading、Legacy Direct 和 external handle lease 计数。
- 保持 ObjectPool 行为不变。
- 本阶段不改变公开 unload 行为。
- 暂时不移动 YooAsset handle dispose 责任；否则 `AssetObject.Release()` 和 `AssetRecord` 可能对同一个 handle 重复 dispose。

### Phase 2：内部 Lease Slot 与 ResourceOwner

- 内部增加基于 handle 的 acquire/release。
- 保留所有旧的返回资产 API 和 `UnloadAsset(object)` 作为兼容 adapter。
- 为 `UnloadAsset(object)` 增加歧义检测。
- 不再把 `ObjectPool.SpawnCount` 当作公开所有权事实。
- 迁移 binding API 前增加 owner generation 校验。
- 启用 lease 驱动释放前，将 YooAsset handle 所有权移动到 `AssetRecord`，或让 `AssetObject` 变成非拥有者。
- 删除 `AssetsReference` 前，先增加 `ResourceOwner` 和 `ResourceBindingService` owner slot。

### Phase 3：从 Prefab 与 Material 路径移除 AssetsReference

- 修改 `LoadGameObject` 和 `LoadGameObjectAsync`，直接实例化并在 `ResourceOwner` 上注册 PrefabSource binding。
- 从 ResourceService prefab 加载中移除 `AssetsReference.Instantiate`。
- 删除 `AssetsReference.Ref(material, gameObject)` 前，先增加 shared material binding slot。
- 在同一个 change set 中，把 prefab source ref 和 shared material ref 迁移到 binding slot。
- 仅对尚未迁移到 binding slot 的 runtime-instance material API，临时保留 `MaterialInstanceReference` 中的 runtime material instance 销毁逻辑。Shared material API 不能再使用 `AssetsReference`。
- 确保 GameObjectPool / UI recycle 路径调用 owner release。
- 编译错误都解决后删除 `AssetsReference`，不要把它保留为兼容 adapter。

### Phase 4：Sprite 与 SubSprite 绑定

- 将 `SetSprite` 迁移到 `ResourceBindingService`。
- 将 `SetSubSprite` 所有权迁移到 Resource 记录，使用父级 `SubAssetsHandle` load key 和子精灵 view key。
- 用 `ResourceOwner` 替代 `SubSpriteReference`。
- 如有需要，保留 `ResourceExtComponent` 作为 facade。

### Phase 5：Material 绑定收尾

- 将 runtime material instance 清理迁移到 binding slot。
- 避免轮询 `Renderer.material`。
- 所有 runtime-instance material API 都把 runtime object ownership 存入 `BindingSlot.RuntimeObject` 后，移除 `MaterialInstanceReference`。

### Phase 6：Raw Handle 与 Debug 语义

- 将原始 `AssetHandle` API 标记为 advanced/legacy，或替换为 Resource 拥有的 external handle lease API。
- ObjectPool inspector 继续用于展示池内部。
- 修改 debug 文案，避免把 ObjectPool 展示成资源所有权事实来源。
- 如有价值，可以把 ObjectPool asset entry 交叉链接到 Resource Debugger 记录。

## 破坏性重构

本设计有意要求以下内部破坏性重构：

- `ResourceService` 的资产生命周期从 ObjectPool `SpawnCount` 语义迁移为 `AssetRecord` 引用计数。
- 所有旧的返回资产 API 和 `UnloadAsset(object)` 都变成基于 legacy direct ref 的兼容 adapter。
- 引入新的 handle/lease API，用于显式所有权。
- 原始 `AssetHandle` API 必须标记为 legacy/advanced，或替换为槽位支撑的 external handle lease API。
- YooAsset handle 所有权必须移动到唯一一层；如果 `AssetRecord` 拥有 handle，`AssetObject.Release()` 不能继续 dispose handle。
- Owner generation 成为 binding/recycle 契约的一部分。
- `ResourceExtComponent` 被重命名、缩减，或替换为 `ResourceBindingService` / `ResourceBindingComponent`。
- `SetSpriteExtensions` 必须迁移到统一 binding pipeline。
- `SetSubSprite` 必须迁移到 Resource 记录中，并且每个 atlas load key 只使用一个父级 `SubAssetsHandle`，不能每个 sub-sprite view key 一个 handle。
- `AssetsSetHelper` 必须把 `SetMaterial`、`SetSharedMaterial` 和 material instance 创建迁移到统一 binding pipeline，并使用显式 shared/instance API。
- `AssetsReference` 被删除，而不是降级为兼容 adapter。
- `SubSpriteReference` 和 `MaterialInstanceReference` 在对应 binding 迁移到 `ResourceBindingService` 后删除。
- Prefab source 引用追踪必须迁移到 `ResourceOwner` 和 lease。
- GameObjectPool 和 UI recycle 路径必须显式释放 `ResourceOwner` bindings。
- Resource 和 ObjectPool debug 面板必须调整，避免继续把 ObjectPool 展示成资源所有权事实来源。
