using System.Reflection;
using System.Runtime.InteropServices;
using Autodesk.AutoCAD.Runtime;
using SteelGrid.Plugin.Commands;

[assembly: AssemblyTitle("SteelGrid.Plugin")]
[assembly: AssemblyDescription("钢格板自动排条 AutoCAD 插件")]
[assembly: AssemblyProduct("SteelGrid.Plugin")]
[assembly: ComVisible(false)]
[assembly: Guid("e5f5a8d6-6c0b-4b6d-8d37-6c0a57a5a002")]
[assembly: AssemblyVersion("1.0.3.0")]
[assembly: AssemblyFileVersion("1.0.3.0")]
[assembly: CommandClass(typeof(GridPluginCommands))]
