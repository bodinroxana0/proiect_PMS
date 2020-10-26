using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NAudio;
using System.Windows.Forms;
using System.IO;
using NAudio.Wave;

namespace Shazam
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            var outputFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "NAudio");
            Directory.CreateDirectory(outputFolder);
            var outputFilePath = Path.Combine(outputFolder, "recorded.wav");
            var waveIn = new WaveInEvent();
            WaveFileWriter writer = null;
            bool closing = false;
            //pentru inregistrare audio
            button1.Click += (s, a) =>
            {
                writer = new WaveFileWriter(outputFilePath, waveIn.WaveFormat);
                waveIn.StartRecording();
                button1.Enabled = false;
                button3.Enabled = true;
            };
            waveIn.DataAvailable += (s, a) =>
            {
                writer.Write(a.Buffer, 0, a.BytesRecorded);
            };
            waveIn.DataAvailable += (s, a) =>
            {
                writer.Write(a.Buffer, 0, a.BytesRecorded);
                if (writer.Position > waveIn.WaveFormat.AverageBytesPerSecond * 30)
                {
                    waveIn.StopRecording();
                }
            };
            //pentru oprire inregistrare
            button3.Click += (s, a) => waveIn.StopRecording();
            this.FormClosing += (s, a) => { closing = true; waveIn.StopRecording(); };

            waveIn.RecordingStopped += (s, a) =>
            {
                writer?.Dispose();
                writer = null;
                button1.Enabled = true;
                button3.Enabled = false;
                if (closing)
                {
                    waveIn.Dispose();
                }
            };
        }
    }
}
