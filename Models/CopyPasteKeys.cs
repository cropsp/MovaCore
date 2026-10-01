namespace MovaCore.Models
{
    /// <summary>The shortcuts used to copy the selection and paste the converted text.</summary>
    public enum CopyPasteKeys
    {
        /// <summary>Ctrl+C / Ctrl+V.</summary>
        CtrlCV,

        /// <summary>Ctrl+Insert / Shift+Insert: in terminals Ctrl+C without a selection would interrupt the program.</summary>
        CtrlInsertShiftInsert,
    }
}
