#:package Mono.Cecil@0.11.5
using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

// args: <modDll> <bridgeDll> <gameDll> <outDll> <managedDir>
var modPath = args[0];
var bridgePath = args[1];
var gamePath = args[2];
var outPath = args[3];
var managedDir = args.Length > 4 ? args[4] : Path.GetDirectoryName(gamePath);

var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(managedDir);
resolver.AddSearchDirectory(Path.GetDirectoryName(modPath));
resolver.AddSearchDirectory(@"F:\Games\coop-ifz\game-a\BepInEx\core");
var rp = new ReaderParameters { AssemblyResolver = resolver, InMemory = true };

var modAsm = AssemblyDefinition.ReadAssembly(modPath, rp);
var mod = modAsm.MainModule;

var bridgeAsm = AssemblyDefinition.ReadAssembly(bridgePath, new ReaderParameters { AssemblyResolver = resolver, InMemory = true });
var getMethod = bridgeAsm.MainModule.Types.First(t => t.Name == "Bridge").Methods.First(m => m.Name == "Get" && m.IsStatic);
var getRef = mod.ImportReference(getMethod);

var gameAsm = AssemblyDefinition.ReadAssembly(gamePath, rp);
var mrType = gameAsm.MainModule.Types.First(t => t.Name == "MainResourcesContainer");
var newCtor = mrType.Methods.First(m => m.IsConstructor && m.Parameters.Count == 5);
var newCtorRef = mod.ImportReference(newCtor);

var type = mod.Types.SelectMany(AllTypes).First(t => t.Name == "PlayerEconomyState");
var ctor = type.Methods.First(m => m.IsConstructor && m.Parameters.Count == 3);
var il = ctor.Body.GetILProcessor();

var newobj = ctor.Body.Instructions.FirstOrDefault(i => i.OpCode == OpCodes.Newobj
    && i.Operand is MethodReference mr && mr.DeclaringType.Name == "MainResourcesContainer");
if (newobj == null) throw new Exception("call site not found");

var intArg = newobj.Previous.Previous;
if (intArg.OpCode != OpCodes.Ldc_I4_0) throw new Exception("unexpected IL before newobj: " + intArg);
il.InsertBefore(intArg, Instruction.Create(OpCodes.Call, getRef));
newobj.Operand = newCtorRef;

var inserted = ctor.Body.Instructions.Any(i => i.OpCode == OpCodes.Call && i.Operand is MethodReference m && m.Name == "Get" && m.DeclaringType.Name == "Bridge");
if (!inserted) throw new Exception("bridge call was not inserted");

Console.WriteLine("patched " + ctor.FullName);
foreach (var i in ctor.Body.Instructions.SkipWhile(i => i.Offset < 0x35).Take(12))
    Console.WriteLine("  " + i);

modAsm.Write(outPath);
Console.WriteLine("written " + outPath + " (" + new FileInfo(outPath).Length + " bytes)");

static System.Collections.Generic.IEnumerable<TypeDefinition> AllTypes(TypeDefinition t)
{
    yield return t;
    foreach (var n in t.NestedTypes.SelectMany(AllTypes)) yield return n;
}
