using System;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MonoMod.Utils;

namespace WildWasteland.Core
{
    // Optional-assembly access is generated once through Cecil: the game's DynamicMethod is stripped.
    internal static class MultiplayerAccess
    {
        internal static Func<T, object> New<T>(Type type)
        {
            var constructor = AccessTools.Constructor(type, new[] { typeof(T) });
            if (constructor == null) throw new MissingMethodException(type.FullName, ".ctor");
            using (var dm = new DynamicMethodDefinition("WW_MP_new_" + type.Name, typeof(object), new[] { typeof(T) }))
            {
                var il = dm.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Newobj, constructor); il.Emit(OpCodes.Ret);
                return (Func<T, object>)Delegate.CreateDelegate(typeof(Func<T, object>), dm.Generate());
            }
        }
        internal static T Call<T>(Type type, string name, params Type[] parameters) where T : class
        {
            var method = AccessTools.Method(type, name, parameters);
            if (method == null) throw new MissingMethodException(type.FullName, name);
            var invoke = typeof(T).GetMethod("Invoke");
            var args = invoke.GetParameters(); var types = new Type[args.Length];
            for (int i = 0; i < args.Length; i++) types[i] = args[i].ParameterType;
            var signature = method.GetParameters(); int instance = method.IsStatic ? 0 : 1;
            if (args.Length != signature.Length + instance || !Compatible(method.ReturnType, invoke.ReturnType)) throw new InvalidOperationException("Incompatible MP method: " + type.FullName + "." + name);
            for (int i = 0; i < signature.Length; i++)
                if (types[i + instance] != typeof(object) && !Compatible(types[i + instance], signature[i].ParameterType)) throw new InvalidOperationException("Incompatible MP argument: " + type.FullName + "." + name);
            using (var dm = new DynamicMethodDefinition("WW_MP_" + name, invoke.ReturnType, types))
            {
                var il = dm.GetILGenerator(); int offset = method.IsStatic ? 0 : 1;
                if (!method.IsStatic) { il.Emit(OpCodes.Ldarg_0); il.Emit(type.IsValueType ? OpCodes.Unbox : OpCodes.Castclass, type); }
                var target = method.GetParameters();
                for (int i = 0; i < target.Length; i++)
                {
                    il.Emit(OpCodes.Ldarg, i + offset);
                    if (types[i + offset] == typeof(object) && target[i].ParameterType != typeof(object))
                        il.Emit(target[i].ParameterType.IsValueType ? OpCodes.Unbox_Any : OpCodes.Castclass, target[i].ParameterType);
                }
                il.Emit(method.IsStatic ? OpCodes.Call : OpCodes.Callvirt, method);
                if (method.ReturnType != invoke.ReturnType && method.ReturnType.IsValueType && invoke.ReturnType == typeof(object)) il.Emit(OpCodes.Box, method.ReturnType);
                il.Emit(OpCodes.Ret);
                return Delegate.CreateDelegate(typeof(T), dm.Generate()) as T;
            }
        }
        static bool Compatible(Type from, Type to)
        { return from == to || to.IsAssignableFrom(from) || (from.IsEnum && Enum.GetUnderlyingType(from) == to) || (to.IsEnum && Enum.GetUnderlyingType(to) == from); }

        internal static Func<object, T> Get<T>(Type type, string name)
        {
            var field = AccessTools.Field(type, name);
            if (field == null)
            {
                var property = AccessTools.Property(type, name);
                if (property == null) throw new MissingMemberException(type.FullName, name);
                var getter = property.GetGetMethod(true);
                if (!getter.IsStatic) return Call<Func<object, T>>(type, getter.Name, Type.EmptyTypes);
                var get = Call<Func<T>>(type, getter.Name, Type.EmptyTypes);
                return ignored => get();
            }
            if (!Compatible(field.FieldType, typeof(T))) throw new InvalidOperationException("Incompatible MP field: " + type.FullName + "." + name);
            using (var dm = new DynamicMethodDefinition("WW_MP_get_" + name, typeof(T), new[] { typeof(object) }))
            {
                var il = dm.GetILGenerator();
                if (!field.IsStatic) { il.Emit(OpCodes.Ldarg_0); il.Emit(field.DeclaringType.IsValueType ? OpCodes.Unbox : OpCodes.Castclass, field.DeclaringType); }
                il.Emit(field.IsStatic ? OpCodes.Ldsfld : OpCodes.Ldfld, field);
                if (field.FieldType.IsValueType && !typeof(T).IsValueType) il.Emit(OpCodes.Box, field.FieldType);
                il.Emit(OpCodes.Ret);
                return (Func<object, T>)Delegate.CreateDelegate(typeof(Func<object, T>), dm.Generate());
            }
        }

        internal static Action<object, T> Set<T>(Type type, string name)
        {
            var field = AccessTools.Field(type, name);
            if (field == null) throw new MissingFieldException(type.FullName, name);
            if (typeof(T) != typeof(object) && !Compatible(typeof(T), field.FieldType)) throw new InvalidOperationException("Incompatible MP field: " + type.FullName + "." + name);
            using (var dm = new DynamicMethodDefinition("WW_MP_set_" + name, typeof(void), new[] { typeof(object), typeof(T) }))
            {
                var il = dm.GetILGenerator();
                if (!field.IsStatic) { il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, field.DeclaringType); }
                il.Emit(OpCodes.Ldarg_1);
                if (typeof(T) == typeof(object) && field.FieldType != typeof(object)) il.Emit(field.FieldType.IsValueType ? OpCodes.Unbox_Any : OpCodes.Castclass, field.FieldType);
                il.Emit(field.IsStatic ? OpCodes.Stsfld : OpCodes.Stfld, field); il.Emit(OpCodes.Ret);
                return (Action<object, T>)Delegate.CreateDelegate(typeof(Action<object, T>), dm.Generate());
            }
        }
    }
}
