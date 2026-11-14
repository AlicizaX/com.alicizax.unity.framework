using System;
using System.Reflection;
using System.Reflection.Emit;
using AlicizaX.UI.Runtime;
using NUnit.Framework;

namespace AlicizaX.UI.Tests
{
    public sealed class UIGeneratedRegistryLoadingTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void FirstLookupRegistersTheAssemblyOnce(bool resourceFirst)
        {
            var fixture = new RegistryAssembly();
            Assert.That(fixture.RegisterCalls, Is.Zero);

            if (resourceFirst)
                Assert.That(UIResRegistry.TryGet(fixture.Holder.TypeHandle, out _), Is.True);

            var info = UIMetaRegistry.Require(fixture.Window);
            Assert.That(info.CacheTime, Is.EqualTo(17));
            Assert.That(info.Create().GetType(), Is.EqualTo(fixture.Window));
            Assert.That(UIMetaRegistry.TryGet(fixture.SecondWindow.FullName, out var second), Is.True);
            Assert.That(second.CacheTime, Is.EqualTo(17));
            Assert.That(UIResRegistry.TryGet(fixture.Holder.TypeHandle, out var resource), Is.True);
            Assert.That(resource.Location, Is.EqualTo("Generated/Holder"));
            Assert.That(UIMetaRegistry.Require(fixture.Window), Is.SameAs(info));
            Assert.That(fixture.RegisterCalls, Is.EqualTo(1));
        }

        [Test]
        public void HolderInAnotherAssemblyRegistersItsOwnResources()
        {
            var resources = new RegistryAssembly();
            var views = new RegistryAssembly(holder: resources.Holder);

            Assert.That(UIMetaRegistry.Require(views.Window).HolderType, Is.EqualTo(resources.Holder));
            Assert.That(views.RegisterCalls, Is.EqualTo(1));
            Assert.That(resources.RegisterCalls, Is.Zero);
            Assert.That(UIResRegistry.TryGet(resources.Holder.TypeHandle, out var resource), Is.True);
            Assert.That(resource.Location, Is.EqualTo("Generated/Holder"));
            Assert.That(resources.RegisterCalls, Is.EqualTo(1));
        }

        [Test]
        public void NameMissingBeforeAssemblyLoadCanBeResolvedAfterwards()
        {
            string name = "UIRegistryTest" + Guid.NewGuid().ToString("N");
            string typeName = name + ".Window";
            Assert.That(Utility.Assembly.GetType(typeName), Is.Null);
            Assert.That(UIMetaRegistry.TryGet(typeName, out _), Is.False);

            var fixture = new RegistryAssembly(name);

            Assert.That(Utility.Assembly.GetType(typeName), Is.EqualTo(fixture.Window));
            Assert.That(UIMetaRegistry.TryGet(typeName, out var info), Is.True);
            Assert.That(info.CacheTime, Is.EqualTo(17));
            Assert.That(fixture.RegisterCalls, Is.EqualTo(1));
        }

        [Test]
        public void AssemblyWithoutGeneratedRegistryUsesReflectionFallback()
        {
            var fixture = new RegistryAssembly(withRegistry: false);
            Assert.That(UIMetaRegistry.TryGet(fixture.Window.TypeHandle, out var info), Is.True);
            Assert.That(info.HolderType, Is.EqualTo(fixture.Holder));
            Assert.That(info.Create().GetType(), Is.EqualTo(fixture.Window));
            Assert.That(UIResRegistry.TryGet(fixture.Holder.TypeHandle, out var resource), Is.True);
            Assert.That(resource.Location, Is.EqualTo("Fallback/Holder"));
            Assert.That(fixture.RegisterCalls, Is.Zero);
        }

        [Test]
        public void FailedRegistrationPropagatesAndDoesNotPreventRetry()
        {
            var fixture = new RegistryAssembly { FailRegistration = true };

            var error = Assert.Throws<TargetInvocationException>(() => UIMetaRegistry.Require(fixture.Window));
            Assert.That(error.InnerException, Is.TypeOf<InvalidOperationException>());
            fixture.FailRegistration = false;

            Assert.That(UIMetaRegistry.Require(fixture.Window).CacheTime, Is.EqualTo(17));
            Assert.That(fixture.RegisterCalls, Is.EqualTo(2));
        }

        // Runtime-created assemblies have no Unity startup callback. The registry matches the generator's
        // internal type/method contract and supplies data that the reflection fallback cannot produce.
        private sealed class RegistryAssembly
        {
            internal readonly Type Holder, Window, SecondWindow;
            internal int RegisterCalls;
            internal bool FailRegistration;
            private readonly bool _ownsHolder;

            internal RegistryAssembly(string name = null, Type holder = null, bool withRegistry = true)
            {
                name ??= "UIRegistryTest" + Guid.NewGuid().ToString("N");
                var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(name), AssemblyBuilderAccess.Run);
                ModuleBuilder module = assembly.DefineDynamicModule(name);
                _ownsHolder = holder == null;
                if (_ownsHolder)
                {
                    TypeBuilder builder = module.DefineType(name + ".Holder", TypeAttributes.Public, typeof(UIHolderObjectBase));
                    builder.SetCustomAttribute(new CustomAttributeBuilder(
                        typeof(UIResAttribute).GetConstructor(new[] { typeof(string), typeof(EUIResLoadType) }),
                        new object[] { "Fallback/Holder", EUIResLoadType.Resources }));
                    holder = builder.CreateType();
                }

                Holder = holder;
                Window = CreateWindow(module, name + ".Window", holder);
                SecondWindow = CreateWindow(module, name + ".SecondWindow", holder);
                if (!withRegistry) return;

                TypeBuilder registry = module.DefineType("AlicizaX.UI.Runtime.UIGeneratedRegistry",
                    TypeAttributes.NotPublic | TypeAttributes.Abstract | TypeAttributes.Sealed);
                FieldBuilder callback = registry.DefineField("Register", typeof(Action), FieldAttributes.Public | FieldAttributes.Static);
                MethodBuilder method = registry.DefineMethod("RegisterAll", MethodAttributes.Assembly | MethodAttributes.Static,
                    typeof(void), Type.EmptyTypes);
                ILGenerator il = method.GetILGenerator();
                il.Emit(OpCodes.Ldsfld, callback);
                il.Emit(OpCodes.Callvirt, typeof(Action).GetMethod(nameof(Action.Invoke)));
                il.Emit(OpCodes.Ret);
                registry.CreateType().GetField("Register").SetValue(null, (Action)RegisterAll);
            }

            private static Type CreateWindow(ModuleBuilder module, string name, Type holder)
            {
                TypeBuilder builder = module.DefineType(name, TypeAttributes.Public, typeof(UIWindow<>).MakeGenericType(holder));
                builder.DefineDefaultConstructor(MethodAttributes.Public);
                return builder.CreateType();
            }

            private void RegisterAll()
            {
                RegisterCalls++;
                if (FailRegistration) throw new InvalidOperationException("Registration failed.");
                if (_ownsHolder) UIResRegistry.Register(Holder, "Generated/Holder", EUIResLoadType.AssetBundle);
                MethodInfo register = typeof(RegistryAssembly).GetMethod(nameof(RegisterWindow), BindingFlags.Static | BindingFlags.NonPublic);
                register.MakeGenericMethod(Window, Holder).Invoke(null, null);
                register.MakeGenericMethod(SecondWindow, Holder).Invoke(null, null);
            }

            private static void RegisterWindow<TView, THolder>()
                where TView : UIWindow<THolder>, new() where THolder : UIHolderObjectBase =>
                UIMetaRegistry.RegisterWindow<TView, THolder>(() => new TView(), UILayer.UI, 17, false);
        }
    }
}
