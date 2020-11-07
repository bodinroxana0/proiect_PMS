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
using Accord.Math;
using System.Numerics;

namespace Shazam
{
    public partial class Form1 : Form
    {
        public static int miliseconds = 0;
        public BufferedWaveProvider bwp;
        public Int32 envelopeMax;

        private int RATE = 44100; // sample rate of the sound card
        private int BUFFERSIZE = (int)Math.Pow(2, 11); // must be a multiple of 2
        public Form1()
        {
            InitializeComponent();
        
            var outputFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "NAudio");
            Directory.CreateDirectory(outputFolder);
            var outputFilePath = Path.Combine(outputFolder, "recorded.wav");
            
            // get the WaveIn class started
            WaveIn waveIn = new WaveIn();
            waveIn.DeviceNumber = 0;
            waveIn.WaveFormat = new NAudio.Wave.WaveFormat(RATE, 1);
            waveIn.BufferMilliseconds = (int)((double)BUFFERSIZE / (double)RATE * 1000.0);
            
            //prepare to store to buffer to plot the audio signal
            bwp = new BufferedWaveProvider(waveIn.WaveFormat);
            bwp.BufferLength = BUFFERSIZE * 2;
            bwp.DiscardOnBufferOverflow = true;

            WaveFileWriter writer = null;
            bool closing = false;
            //pentru inregistrare audio
            button1.Click += (s, a) =>
            {
                UpdateAudioGraph();
                //start timer
                timer1.Enabled = true;
                //prepare to write to wav file
                writer = new WaveFileWriter(outputFilePath, waveIn.WaveFormat);
                
                //start recording
                waveIn.StartRecording();
                
                button1.Enabled = false;
                button3.Enabled = true;
            };
            waveIn.DataAvailable += (s, a) =>
            {
                writer.Write(a.Buffer, 0, a.BytesRecorded);
                bwp.AddSamples(a.Buffer, 0, a.BytesRecorded);
                if (writer.Position > waveIn.WaveFormat.AverageBytesPerSecond * 30)
                {
                    waveIn.StopRecording();
                }
            };
            //pentru oprire inregistrare
            button3.Click += (s, a) =>
            {
                //stop timer
                timer1.Enabled = false;
                waveIn.StopRecording();
            };
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
        
        private void Form1_Load(object sender, EventArgs e)
        {

        }
        public void UpdateAudioGraph()
        {
            // read the bytes from the stream
            int frameSize = BUFFERSIZE;
            var frames = new byte[frameSize];
            bwp.Read(frames, 0, frameSize);
            if (frames.Length == 0) return;
            if (frames[frameSize - 2] == 0) return;
            

            // convert it to int32 manually (and a double for scottplot)
            int SAMPLE_RESOLUTION = 16;
            int BYTES_PER_POINT = SAMPLE_RESOLUTION / 8;
            Int32[] vals = new Int32[frames.Length / BYTES_PER_POINT];
            double[] Ys = new double[frames.Length / BYTES_PER_POINT];
            double[] Xs = new double[frames.Length / BYTES_PER_POINT];
            double[] Ys2 = new double[frames.Length / BYTES_PER_POINT];
            double[] Xs2 = new double[frames.Length / BYTES_PER_POINT];
            for (int i = 0; i < vals.Length; i++)
            {
                // bit shift the byte buffer into the right variable format
                byte hByte = frames[i * 2 + 1];
                byte lByte = frames[i * 2 + 0];
                vals[i] = (int)(short)((hByte << 8) | lByte);
                Xs[i] = i;
                Ys[i] = vals[i];
                Xs2[i] = (double)i / Ys.Length * RATE / 1000.0; // units are in kHz
            }
            
            // update scottplot (PCM, time domain)
            scottPlotUC1.Xs = Xs;
            scottPlotUC1.Ys = Ys;

            //update scottplot (FFT, frequency domain)
            Ys2 = FFT(Ys);
            scottPlotUC2.Xs = Xs2.Take(Xs2.Length / 2).ToArray();
            scottPlotUC2.Ys = Ys2.Take(Ys2.Length / 2).ToArray();


            // update the displays
            scottPlotUC1.UpdateGraph();
            scottPlotUC2.UpdateGraph();

            Application.DoEvents();
            scottPlotUC1.Update();
            scottPlotUC2.Update();
            

        }
        public double[] FFT(double[] data)
        {
            double[] fft = new double[data.Length]; // this is where we will store the output (fft)
            Complex[] fftComplex = new Complex[data.Length]; // the FFT function requires complex format
            for (int i = 0; i < data.Length; i++)
            {
                fftComplex[i] = new Complex(data[i], 0.0); // make it complex format (imaginary = 0)
            }
            Accord.Math.FourierTransform.FFT(fftComplex, Accord.Math.FourierTransform.Direction.Forward);
            for (int i = 0; i < data.Length; i++)
            {
                fft[i] = fftComplex[i].Magnitude; // back to double
                //fft[i] = Math.Log10(fft[i]); // convert to dB
            }
            return fft;
            //todo: this could be much faster by reusing variables
        }
        private void timer1_Tick_1(object sender, EventArgs e)
        {
            miliseconds++;
            label2.Text = (timer1.Interval*miliseconds).ToString();
            UpdateAudioGraph();
        }
    }
}
