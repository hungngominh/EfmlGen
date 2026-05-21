using System;

namespace EfmlGen.Vsix
{
    /// <summary>
    /// Stable GUIDs cho VSIX. Phải khớp với <c>source.extension.vsixmanifest</c> và <c>.vsct</c>.
    /// Không đổi sau khi extension đã release — VS dùng để định danh package + commands.
    /// </summary>
    internal static class PackageGuids
    {
        public const string PackageGuidString = "bf990f5e-756c-4eab-a401-18dddfbf8cb2";
        public const string CommandSetGuidString = "7e832c82-48bc-464b-b0e4-523f5229965d";
        public const string ToolWindowGuidString = "3ab87f8c-cfc7-4ca8-85d8-52fa768a2590";
        public const string WizardGuidString = "f48ce492-d8df-4a7d-a892-85bbcbb825cf";

        public static readonly Guid PackageGuid = new Guid(PackageGuidString);
        public static readonly Guid CommandSetGuid = new Guid(CommandSetGuidString);
        public static readonly Guid ToolWindowGuid = new Guid(ToolWindowGuidString);
        public static readonly Guid WizardGuid = new Guid(WizardGuidString);

        // Command IDs — match values in EfmlGenCommands.vsct (Phase 3+)
        public const int CmdIdUpdateFromDb = 0x0100;
        public const int CmdIdGenerateCode = 0x0101;
        public const int CmdIdShowToolWindow = 0x0102;
        public const int CmdIdSmokeTest = 0x0103;
    }
}
