namespace WinTimeReSyncSvc
{
    partial class WinTimeReSyncSvc
    {
        /// <summary> 
        /// Variável de designer necessária.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Limpar os recursos que estão sendo usados.
        /// </summary>
        /// <param name="disposing">true se for necessário descartar os recursos gerenciados; caso contrário, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Código gerado pelo Designer de Componentes

        /// <summary> 
        /// Método necessário para suporte ao Designer - não modifique 
        /// o conteúdo deste método com o editor de código.
        /// </summary>
        private void InitializeComponent()
        {
            this._eventLog1 = new System.Diagnostics.EventLog();
            ((System.ComponentModel.ISupportInitialize)(this._eventLog1)).BeginInit();
            // 
            // _eventLog1
            // 
            this._eventLog1.Log = "Application";
            this._eventLog1.Source = "WinTimeReSyncSvc";
            this._eventLog1.EntryWritten += new System.Diagnostics.EntryWrittenEventHandler(this.eventLog1_EntryWritten);
            // 
            // WinTimeReSyncSvc
            // 
            this.ServiceName = "WinTimeReSyncSvc";
            ((System.ComponentModel.ISupportInitialize)(this._eventLog1)).EndInit();

        }

        #endregion

        private System.Diagnostics.EventLog _eventLog1;
    }
}
