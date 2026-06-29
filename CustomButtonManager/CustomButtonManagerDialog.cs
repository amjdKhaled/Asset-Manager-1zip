using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace Laserfiche.Samples
{
    public partial class CustomButtonManagerDialog : Form
    {
        public CustomButtonManagerDialog()
        {
            InitializeComponent();
        }

        private void DisableButtons()
        {
            ButtonAddToolbar.Enabled = false;
            ButtonRemoveToolbar.Enabled = false;
            buttonLaunchClient.Enabled = false;
            UseWaitCursor = true;
        }

        private void EnableButtons()
        {
            ButtonAddToolbar.Enabled = true;
            ButtonRemoveToolbar.Enabled = true;
            buttonLaunchClient.Enabled = true;
            UseWaitCursor = false;
        }

        private void ButtonAddToolbar_Click(object sender, EventArgs e)
        {
            DisableButtons();

            try
            {
                CustomButtonManagerApp.SetupToolbar(false);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }

            EnableButtons();
        }

        private void ButtonRemoveToolbar_Click(object sender, EventArgs e)
        {
            DisableButtons();
            
            try
            {
                CustomButtonManagerApp.RemoveToolbar(false);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }

            EnableButtons();
        }

        private void buttonLaunchClient_Click(object sender, EventArgs e)
        {
            DisableButtons();

            try
            {
                CustomButtonManagerApp.LaunchClient();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }

            EnableButtons();
        }
    }
}
