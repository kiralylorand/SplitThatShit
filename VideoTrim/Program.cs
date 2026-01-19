using System;
using System.Windows.Forms;

namespace VideoTrim;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        try
        {
            Application.Run(new MainForm());
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.ToString(),
                "Split That Sh!t - Unexpected error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
