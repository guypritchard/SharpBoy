using System.Diagnostics;
using System.IO;

namespace GB.Experiments.Windows;

/// <summary>The experimental front end owns discovery and launch; the ROM player has no lab dependency.</summary>
internal sealed class ExperimentLauncherForm : Form
{
    internal static readonly (string Title, string Description, string Mode)[] Experiments =
    [
        ("Living world", "Networked Conway simulation across up to 1,000 native Game Boys.", "--life"),
        ("Parallel Mandelbrot", "Native tile rendering, TCP collection and per-machine inspection.", "--cluster"),
        ("DHCP cartridge", "Watch SDK-built firmware acquire an address on the virtual network.", "--dhcp")
    ];

    internal ExperimentLauncherForm()
    {
        Text = "SharpBoy experiments"; ClientSize = new Size(690, 485);
        MinimumSize = new Size(580, 450); StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(9, 18, 31); ForeColor = Color.FromArgb(230, 240, 250);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 10);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 5 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
        layout.Controls.Add(new Label { Text = "SharpBoy experiments\nNative Game Boy firmware, networking and parallel processing.",
            Dock = DockStyle.Fill, AutoSize = false, Font = new Font("Segoe UI", 13) }, 0, 0);
        int row = 1;
        foreach (var experiment in Experiments)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33f));
            var button = new Button { Text = experiment.Title + "\n" + experiment.Description, Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 12), Padding = new Padding(14), TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.FromArgb(17, 30, 46), ForeColor = ForeColor, FlatStyle = FlatStyle.Flat };
            button.FlatAppearance.BorderColor = Color.FromArgb(60, 117, 103);
            button.Click += (_, _) => Launch(experiment.Mode);
            layout.Controls.Add(button, 0, row++);
        }
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.Controls.Add(new Label { Text = "Use command-line options for custom grids or a physical Ethernet adapter.", AutoSize = false, Dock = DockStyle.Fill }, 0, 4);
        Controls.Add(layout);
    }
    private void Launch(string mode)
    {
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "GB.Experiments.Windows.exe")) { UseShellExecute = false };
        start.ArgumentList.Add(mode);
        try
        {
            using var process = Process.Start(start) ?? throw new IOException("The experiment did not start.");
            Close();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not launch the experiment", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}
