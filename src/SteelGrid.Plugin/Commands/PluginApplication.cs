using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

namespace SteelGrid.Plugin.Commands
{
    /// <summary>NETLOAD 后提供可见的加载反馈。</summary>
    public sealed class PluginApplication : IExtensionApplication
    {
        public void Initialize()
        {
            var document = Application.DocumentManager.MdiActiveDocument;
            if (document == null)
            {
                return;
            }

            var editor = document.Editor;
            editor.WriteMessage("\n钢格板自动排条插件 3.01 已加载。可用命令：GPGRIDINFO、GPGRIDSET、GPGRID、GPGRIDNOBORDER。");
        }

        public void Terminate()
        {
        }
    }
}
