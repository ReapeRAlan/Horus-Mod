using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HorusMod.Logging;

namespace HorusMod.Server
{
    /// <summary>
    /// Optional Nuclei adapter built at runtime so Horus never takes a binary dependency on
    /// Nuclei. Only read-only status and diagnostics commands are registered.
    /// </summary>
    internal static class HorusNucleiBridge
    {
        private static Func<string> statusProvider;
        private static Func<string> diagnosticsProvider;
        private static MethodInfo sendPrivateMessage;
        private static bool registered;
        private static bool incompatible;
        private static int dynamicTypeOrdinal;

        public static bool TryRegister(Func<string> status,Func<string> diagnostics)
        {
            if(registered)return true;
            if(incompatible)return false;
            Assembly nuclei=AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(assembly=>string.Equals(assembly.GetName().Name,"MaxWasUnavailable.Nuclei",StringComparison.Ordinal));
            if(nuclei==null)return false;
            try
            {
                Type commandInterface=nuclei.GetType("Nuclei.Features.Commands.ICommand",true);
                Type commandService=nuclei.GetType("Nuclei.Features.Commands.CommandService",true);
                Type chatService=nuclei.GetType("Nuclei.Features.ChatService",true);
                sendPrivateMessage=chatService.GetMethods(BindingFlags.Public|BindingFlags.Static).First(method=>method.Name=="SendPrivateChatMessage"&&method.GetParameters().Length==2&&method.GetParameters()[0].ParameterType==typeof(string));
                MethodInfo register=commandService.GetMethods(BindingFlags.Public|BindingFlags.Static).First(method=>method.Name=="RegisterCommand"&&method.GetParameters().Length==1&&method.GetParameters()[0].ParameterType==commandInterface);
                Type permissionType=commandInterface.GetProperty("PermissionLevel").PropertyType;
                object everyone=Enum.Parse(permissionType,"Everyone",true);
                object admin=Enum.Parse(permissionType,"Admin",true);
                statusProvider=status;diagnosticsProvider=diagnostics;
                register.Invoke(null,new[]{BuildCommand(commandInterface,"horusstatus","Show Horus dedicated-server status.","horusstatus",everyone,0)});
                register.Invoke(null,new[]{BuildCommand(commandInterface,"horusdiagnostics","Show Horus protocol and synchronization diagnostics.","horusdiagnostics",admin,1)});
                registered=true;HorusLog.Info("Server","Registered read-only horusstatus and horusdiagnostics commands with Nuclei.");return true;
            }
            catch(Exception ex){incompatible=true;HorusLog.Warning("Server","Nuclei was detected but its command API was incompatible: "+(ex.InnerException?.Message??ex.Message));return false;}
        }

        private static object BuildCommand(Type commandInterface,string name,string description,string usage,object permission,int provider)
        {
            AssemblyName assemblyName=new AssemblyName("Horus.Nuclei.Dynamic."+(++dynamicTypeOrdinal));
            AssemblyBuilder assembly=AssemblyBuilder.DefineDynamicAssembly(assemblyName,AssemblyBuilderAccess.Run);
            ModuleBuilder module=assembly.DefineDynamicModule(assemblyName.Name);
            TypeBuilder type=module.DefineType("HorusNucleiCommand"+dynamicTypeOrdinal,TypeAttributes.Public|TypeAttributes.Sealed);
            type.AddInterfaceImplementation(commandInterface);type.DefineDefaultConstructor(MethodAttributes.Public);
            ImplementStringProperty(type,commandInterface,"Name",name);
            ImplementStringProperty(type,commandInterface,"Description",description);
            ImplementStringProperty(type,commandInterface,"Usage",usage);
            ImplementEnumProperty(type,commandInterface,"PermissionLevel",permission);
            MethodInfo validateInterface=commandInterface.GetMethod("Validate");
            ImplementForwarder(type,validateInterface,typeof(HorusNucleiBridge).GetMethod(nameof(ValidateNoArguments),BindingFlags.NonPublic|BindingFlags.Static),-1);
            MethodInfo executeInterface=commandInterface.GetMethod("Execute");
            ImplementForwarder(type,executeInterface,typeof(HorusNucleiBridge).GetMethod(nameof(Execute),BindingFlags.NonPublic|BindingFlags.Static),provider);
            return Activator.CreateInstance(type.CreateType());
        }

        private static void ImplementStringProperty(TypeBuilder type,Type contract,string name,string value)
        {
            MethodInfo target=contract.GetProperty(name).GetGetMethod();
            MethodBuilder getter=type.DefineMethod(target.Name,MethodAttributes.Public|MethodAttributes.Virtual|MethodAttributes.SpecialName|MethodAttributes.HideBySig,typeof(string),Type.EmptyTypes);
            ILGenerator il=getter.GetILGenerator();il.Emit(OpCodes.Ldstr,value);il.Emit(OpCodes.Ret);type.DefineMethodOverride(getter,target);
        }
        private static void ImplementEnumProperty(TypeBuilder type,Type contract,string name,object value)
        {
            MethodInfo target=contract.GetProperty(name).GetGetMethod();
            MethodBuilder getter=type.DefineMethod(target.Name,MethodAttributes.Public|MethodAttributes.Virtual|MethodAttributes.SpecialName|MethodAttributes.HideBySig,target.ReturnType,Type.EmptyTypes);
            ILGenerator il=getter.GetILGenerator();il.Emit(OpCodes.Ldc_I4,Convert.ToInt32(value));il.Emit(OpCodes.Ret);type.DefineMethodOverride(getter,target);
        }
        private static void ImplementForwarder(TypeBuilder type,MethodInfo target,MethodInfo callback,int provider)
        {
            Type[] parameters=target.GetParameters().Select(parameter=>parameter.ParameterType).ToArray();
            MethodBuilder method=type.DefineMethod(target.Name,MethodAttributes.Public|MethodAttributes.Virtual|MethodAttributes.HideBySig,target.ReturnType,parameters);
            ILGenerator il=method.GetILGenerator();if(provider>=0)il.Emit(OpCodes.Ldc_I4,provider);il.Emit(OpCodes.Ldarg_1);il.Emit(OpCodes.Ldarg_2);il.Emit(OpCodes.Call,callback);il.Emit(OpCodes.Ret);type.DefineMethodOverride(method,target);
        }
        private static bool ValidateNoArguments(object player,string[] args)=>args==null||args.Length==0;
        private static bool Execute(int provider,object player,string[] args)
        {
            try{string message=(provider==0?statusProvider:diagnosticsProvider)?.Invoke()??"Horus status unavailable.";sendPrivateMessage.Invoke(null,new[]{message,player});return true;}
            catch(Exception ex){HorusLog.Warning("Server","Nuclei status response failed: "+(ex.InnerException?.Message??ex.Message));return false;}
        }
    }
}
