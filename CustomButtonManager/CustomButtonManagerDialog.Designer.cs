namespace Laserfiche.Samples
{
    partial class CustomButtonManagerDialog
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.ButtonAddToolbar = new System.Windows.Forms.Button();
            this.ButtonRemoveToolbar = new System.Windows.Forms.Button();
            this.buttonLaunchClient = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // ButtonAddToolbar
            // 
            this.ButtonAddToolbar.Location = new System.Drawing.Point(26, 28);
            this.ButtonAddToolbar.Name = "ButtonAddToolbar";
            this.ButtonAddToolbar.Size = new System.Drawing.Size(109, 23);
            this.ButtonAddToolbar.TabIndex = 0;
            this.ButtonAddToolbar.Text = "Add Toolbar";
            this.ButtonAddToolbar.UseVisualStyleBackColor = true;
            this.ButtonAddToolbar.Click += new System.EventHandler(this.ButtonAddToolbar_Click);
            // 
            // ButtonRemoveToolbar
            // 
            this.ButtonRemoveToolbar.Location = new System.Drawing.Point(26, 58);
            this.ButtonRemoveToolbar.Name = "ButtonRemoveToolbar";
            this.ButtonRemoveToolbar.Size = new System.Drawing.Size(109, 23);
            this.ButtonRemoveToolbar.TabIndex = 1;
            this.ButtonRemoveToolbar.Text = "Remove Toolbar";
            this.ButtonRemoveToolbar.UseVisualStyleBackColor = true;
            this.ButtonRemoveToolbar.Click += new System.EventHandler(this.ButtonRemoveToolbar_Click);
            // 
            // buttonLaunchClient
            // 
            this.buttonLaunchClient.Location = new System.Drawing.Point(26, 88);
            this.buttonLaunchClient.Name = "buttonLaunchClient";
            this.buttonLaunchClient.Size = new System.Drawing.Size(109, 23);
            this.buttonLaunchClient.TabIndex = 2;
            this.buttonLaunchClient.Text = "Launch Client";
            this.buttonLaunchClient.UseVisualStyleBackColor = true;
            this.buttonLaunchClient.Click += new System.EventHandler(this.buttonLaunchClient_Click);
            // 
            // CustomButtonManagerDialog
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(165, 153);
            this.Controls.Add(this.buttonLaunchClient);
            this.Controls.Add(this.ButtonRemoveToolbar);
            this.Controls.Add(this.ButtonAddToolbar);
            this.Name = "CustomButtonManagerDialog";
            this.Text = "CustomButtonManagerDialog";
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Button ButtonAddToolbar;
        private System.Windows.Forms.Button ButtonRemoveToolbar;
        private System.Windows.Forms.Button buttonLaunchClient;
    }
}