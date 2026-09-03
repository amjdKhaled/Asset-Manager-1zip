// Compile-only surface for clean build agents that do not have the proprietary
// Laserfiche SDK installed. The assembly is never copied into release output;
// the real ClientAutomation.dll is provided by Laserfiche Desktop Client.

using System;

namespace Laserfiche.ClientAutomation
{
    public enum ClientWindowType { Main }
    public enum ToolbarPosition { Top }

    public sealed class CustomButtonInfo
    {
        public string Description { get; set; }
        public string Command { get; set; }
        public string IconPath { get; set; }
    }

    public sealed class ToolbarButtonInfo
    {
        public int Id { get; set; }
        public bool IsSeparator { get; set; }
    }

    public sealed class ClientManager : IDisposable
    {
        public ToolbarManager GetToolbarManager(ClientWindowType windowType) =>
            throw new NotSupportedException("Reference assembly only.");

        public void Dispose() { }
    }

    public sealed class ToolbarManager : IDisposable
    {
        public void AddToolbar(string name, ToolbarPosition position) =>
            throw new NotSupportedException("Reference assembly only.");
        public int AddCustomToolbarButton(CustomButtonInfo info) =>
            throw new NotSupportedException("Reference assembly only.");
        public void AddButton(string toolbarName, ToolbarButtonInfo info, int index) =>
            throw new NotSupportedException("Reference assembly only.");
        public int GetToolbarCount() => throw new NotSupportedException("Reference assembly only.");
        public string GetToolbarName(int index) => throw new NotSupportedException("Reference assembly only.");
        public void DeleteToolbar(string name) => throw new NotSupportedException("Reference assembly only.");
        public int GetCustomToolbarButtonCount() => throw new NotSupportedException("Reference assembly only.");
        public CustomButtonInfo GetCustomToolbarButton(int index) =>
            throw new NotSupportedException("Reference assembly only.");
        public void RemoveCustomToolbarButton(int index) =>
            throw new NotSupportedException("Reference assembly only.");
        public void Dispose() { }
    }
}
