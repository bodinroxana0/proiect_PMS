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
using NAudio.Dsp; // for FastFourierTransform
using System.Drawing.Imaging; // for ImageLockMode
using System.Runtime.InteropServices; // for Marshal
using SoundFingerprinting;
using SoundFingerprinting.Audio;
using SoundFingerprinting.InMemory;
using SoundFingerprinting.Builder;
using SoundFingerprinting.DAO.Data;
using SoundFingerprinting.Data;

namespace Shazam
{
    public partial class Form1 : Form
    {
        public static int miliseconds = 0;
        public BufferedWaveProvider bwp;
        public Int32 envelopeMax;
        private int RATE = 44100; // sample rate of the sound card
        private int BUFFERSIZE = (int)Math.Pow(2, 11); // must be a multiple of 2

        //spectograma
        private static int buffers_captured = 0; // total number of audio buffers filled
        private static int buffers_remaining = 0; // number of buffers which have yet to be analyzed
        private static double unanalyzed_max_sec= 2.5; // maximum amount of unanalyzed audio to maintain in memory
        private static List<short> unanalyzed_values = new List<short>(); // audio data lives here waiting to be analyzed
        private static List<List<double>> spec_data; // columns are time points, rows are frequency points
        private static int spec_width = 600;
        int fft_size = 4096;
        private static int spec_height;
        int pixelsPerBuffer= 10;
        private static Random rand = new Random();
        private int rate= 44100;
        private int buffer_update_hz= 20;

        //amprenta
        private readonly SoundFingerprinting.DAO.IModelReference trackReference;
        private readonly IModelService modelService = new InMemoryModelService(); // store fingerprints in RAM
        private readonly IAudioService audioService = new SoundFingerprintingAudioService(); // default audio library


        public Form1()
        {
            InitializeComponent();
            
            var outputFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "NAudio");
            System.IO.Directory.CreateDirectory(outputFolder);
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

                // sound card configuration
                spec_height = fft_size / 2;
                // fill spectrogram data with empty values
                spec_data = new List<List<double>>();
                List<double> data_empty = new List<double>();
                for (int i = 0; i < spec_height; i++) data_empty.Add(0);
                for (int i = 0; i < spec_width; i++) spec_data.Add(data_empty);
                // resize picturebox to accomodate data shape
                pictureBox1.Width = spec_data.Count;
                pictureBox1.Height = spec_data[0].Count;
                pictureBox1.Location = new Point(0, 0);
                
               // waveIn.BufferMilliseconds = 1000 / buffer_update_hz;
            };
            waveIn.DataAvailable += (s, a) =>
            {
                writer.Write(a.Buffer, 0, a.BytesRecorded);
                bwp.AddSamples(a.Buffer, 0, a.BytesRecorded);
                if (writer.Position > waveIn.WaveFormat.AverageBytesPerSecond * 30)
                {
                    waveIn.StopRecording();
                }
            buffers_captured += 1;
            buffers_remaining += 1;

            // interpret as 16 bit audio, so each two bytes become one value
            short[] values = new short[a.Buffer.Length / 2];
            for (int i = 0; i < a.BytesRecorded; i += 2)
            {
                values[i / 2] = (short)((a.Buffer[i + 1] << 8) | a.Buffer[i + 0]);
            }

            // add these values to the growing list, but ensure it doesn't get too big
            unanalyzed_values.AddRange(values);

            int unanalyzed_max_count = (int)unanalyzed_max_sec * rate;

            if (unanalyzed_values.Count > unanalyzed_max_count)
            {
                unanalyzed_values.RemoveRange(0, unanalyzed_values.Count - unanalyzed_max_count);
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
            System.Numerics.Complex[] fftComplex = new System.Numerics.Complex[data.Length]; // the FFT function requires complex format
            for (int i = 0; i < data.Length; i++)
            {
                fftComplex[i] = new System.Numerics.Complex(data[i], 0.0); // make it complex format (imaginary = 0)
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
        void Analyze_values()
        {
            if (fft_size == 0) return;
            if (unanalyzed_values.Count < fft_size) return;
            while (unanalyzed_values.Count >= fft_size) Analyze_chunk();
            
        }
        void Analyze_chunk()
        {
            // fill data with FFT info
            short[] data = new short[fft_size];
            data = unanalyzed_values.GetRange(0, fft_size).ToArray();

            // remove the left-most (oldest) column of data
            spec_data.RemoveAt(0);

            // insert new data to the right-most (newest) position
            List<double> new_data = new List<double>();

            // prepare the complex data which will be FFT'd
            NAudio.Dsp.Complex[] fft_buffer = new NAudio.Dsp.Complex[fft_size];
            for (int i = 0; i < fft_size; i++)
            {
                //fft_buffer[i].X = (float)unanalyzed_values[i]; // no window
                fft_buffer[i].X = (float)(unanalyzed_values[i] * FastFourierTransform.HammingWindow(i, fft_size));
                fft_buffer[i].Y = 0;
            }

            // perform the FFT
            FastFourierTransform.FFT(true, (int)Math.Log(fft_size, 2.0), fft_buffer);

            // fill that new data list with fft values
            for (int i = 0; i < spec_data[spec_data.Count - 1].Count; i++)
            {
                double val;
                val = (double)fft_buffer[i].X + (double)fft_buffer[i].Y;
                val = Math.Abs(val);
                new_data.Add(val);
            }

            new_data.Reverse();
            spec_data.Insert(spec_data.Count, new_data); // replaces, doesn't append!

            // remove a certain amount of unanalyzed data
            unanalyzed_values.RemoveRange(0, fft_size / pixelsPerBuffer);

        }
        void Update_bitmap_with_data()
        {
            // create a bitmap we will work with
            Bitmap bitmap = new Bitmap(spec_data.Count, spec_data[0].Count, PixelFormat.Format8bppIndexed);

            // modify the indexed palette to make it grayscale
            ColorPalette pal = bitmap.Palette;
            for (int i = 0; i < 256; i++)
                pal.Entries[i] = Color.FromArgb(255, i, i, i);
            bitmap.Palette = pal;

            // prepare to access data via the bitmapdata object
            BitmapData bitmapData = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                                                    ImageLockMode.ReadOnly, bitmap.PixelFormat);

            // create a byte array to reflect each pixel in the image
            byte[] pixels = new byte[bitmapData.Stride * bitmap.Height];

            // fill pixel array with data
            for (int col = 0; col < spec_data.Count; col++)
            {
                double scaleFactor;
                scaleFactor = 8;
                for (int row = 0; row < spec_data[col].Count; row++)
                {
                    int bytePosition = row * bitmapData.Stride + col;
                    double pixelVal = spec_data[col][row] * scaleFactor;
                    pixelVal = Math.Max(0, pixelVal);
                    pixelVal = Math.Min(255, pixelVal);
                    pixels[bytePosition] = (byte)(pixelVal);
                }
            }

            // turn the byte array back into a bitmap
            Marshal.Copy(pixels, 0, bitmapData.Scan0, pixels.Length);
            bitmap.UnlockBits(bitmapData);

            // apply the bitmap to the picturebox
            pictureBox1.Image = bitmap;
        }
        private void timer1_Tick_1(object sender, EventArgs e)
        {
            miliseconds++;
            label2.Text = (timer1.Interval*miliseconds).ToString();

            Analyze_values();
            Update_bitmap_with_data();
            UpdateAudioGraph();
        }
        private void scottPlotUC2_Load(object sender, EventArgs e)
        {

        }
        private void label6_Click(object sender, EventArgs e)
        {

        }
        
        public async Task StoreForLaterRetrieval(string pathToAudioFile)
        {
            if (pathToAudioFile.Contains("song1"))
            {
                var track = new TrackInfo("TCADX1833623", "Fur_Elise", "Beethoven", 10000);

                // create fingerprints
                var hashedFingerprints = await FingerprintCommandBuilder.Instance
                                            .BuildFingerprintCommand()
                                            .From(pathToAudioFile)
                                            .UsingServices(audioService)
                                            .Hash();

                // store hashes in the database for later retrieval
                modelService.Insert(track, hashedFingerprints);
            }
            else if(pathToAudioFile.Contains("song2"))
            {
                var track = new TrackInfo("TCADX1833624", "River_Flows_In_You", "Yiruma", 20000);

                // create fingerprints
                var hashedFingerprints = await FingerprintCommandBuilder.Instance
                                            .BuildFingerprintCommand()
                                            .From(pathToAudioFile)
                                            .UsingServices(audioService)
                                            .Hash();

                // store hashes in the database for later retrieval
                modelService.Insert(track, hashedFingerprints);
            }
            else if (pathToAudioFile.Contains("song3"))
            {
                var track = new TrackInfo("TCADX1833624", "Swimming", "Hans_Zimmer", 20000);

                // create fingerprints
                var hashedFingerprints = await FingerprintCommandBuilder.Instance
                                            .BuildFingerprintCommand()
                                            .From(pathToAudioFile)
                                            .UsingServices(audioService)
                                            .Hash();

                // store hashes in the database for later retrieval
                modelService.Insert(track, hashedFingerprints);
            }
            else if (pathToAudioFile.Contains("song4"))
            {
                var track = new TrackInfo("TCADX1833625", "Piano_Violin", "HipHopBeat", 20000);

                // create fingerprints
                var hashedFingerprints = await FingerprintCommandBuilder.Instance
                                            .BuildFingerprintCommand()
                                            .From(pathToAudioFile)
                                            .UsingServices(audioService)
                                            .Hash();

                // store hashes in the database for later retrieval
                modelService.Insert(track, hashedFingerprints);
            }
            else if (pathToAudioFile.Contains("song5"))
            {
                var track = new TrackInfo("TCADX1833626", "Crystallize", "Lindsey_Stirling", 20000);

                // create fingerprints
                var hashedFingerprints = await FingerprintCommandBuilder.Instance
                                            .BuildFingerprintCommand()
                                            .From(pathToAudioFile)
                                            .UsingServices(audioService)
                                            .Hash();

                // store hashes in the database for later retrieval
                modelService.Insert(track, hashedFingerprints);
            }

        }
        public async Task<SoundFingerprinting.Query.QueryResult> GetBestMatchForSong(string queryAudioFile)
        {
            int secondsToAnalyze = 10; // number of seconds to analyze from query file
            int startAtSecond = 0; // start at the begining

            // query the underlying database for similar audio sub-fingerprints
            var queryResult=await QueryCommandBuilder.Instance.BuildQueryCommand()
                                                 .From(queryAudioFile, secondsToAnalyze, startAtSecond)
                                                 .UsingServices(modelService, audioService)
                                                 .Query();
            return queryResult;
        }

        private async void button2_Click(object sender, EventArgs e)
        {
            var outputFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "NAudio");
            var song1 = Path.Combine(outputFolder, "song1.wav");
            var song2 = Path.Combine(outputFolder, "song2.wav");
            var song3 = Path.Combine(outputFolder, "song3.wav");
            var song4 = Path.Combine(outputFolder, "song4.wav");
            var song5 = Path.Combine(outputFolder, "song5.wav");
            var recorded_sample = Path.Combine(outputFolder, "recorded.wav");
            await StoreForLaterRetrieval(song1);
            await StoreForLaterRetrieval(song2);
            await StoreForLaterRetrieval(song3);
            await StoreForLaterRetrieval(song4);
            await StoreForLaterRetrieval(song5);
            
            SoundFingerprinting.Query.QueryResult result=await GetBestMatchForSong(recorded_sample);
            if (result.ContainsMatches)
            {
                List<SoundFingerprinting.Query.ResultEntry> list = result.ResultEntries.ToList();
                MessageBox.Show("Melodia a fost găsită! \n Artist- " + list[0].Track.Artist + " Melodie- " + list[0].Track.Title);
            }
            else
            {
                MessageBox.Show("Melodia nu a fost găsită!");
            }


        }
    }
}
