using System;

namespace WastelandMPNew
{
    /// <summary>Prevents a title-menu HOST click from opening networking against an unbound or stale save.</summary>
    public sealed class HostLaunchGate
    {
        Guid attempt;
        string selectedSave;
        bool pending;

        /// <summary>Starts one explicit selected-world launch; save identity is an opaque Saves-issued id, never a guest path.</summary>
        public Guid Begin(string saveIdentity)
        {
            if (pending || string.IsNullOrWhiteSpace(saveIdentity)) throw new InvalidOperationException("Select one world before hosting");
            selectedSave = saveIdentity; pending = true; attempt = Guid.NewGuid(); return attempt;
        }

        /// <summary>Grants networking once for the exact loaded save after Core reports world-ready and no active transaction.</summary>
        public bool ConfirmReady(Guid token, string loadedSaveIdentity, bool worldReady, bool savingOrLoading)
        {
            if (!pending || token != attempt || !worldReady || savingOrLoading || !string.Equals(selectedSave, loadedSaveIdentity, StringComparison.Ordinal)) return false;
            pending = false; selectedSave = null; attempt = Guid.Empty; return true;
        }

        /// <summary>Cancels failed or user-aborted launch; delayed callbacks can no longer grant networking.</summary>
        public void Cancel()
        {
            pending = false; selectedSave = null; attempt = Guid.Empty;
        }
    }
}
