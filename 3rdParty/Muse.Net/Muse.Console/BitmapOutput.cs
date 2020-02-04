﻿using System;
using System.Threading.Tasks;
using Harthoorn.MuseClient;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;

namespace ConsoleApp
{

    public static class BitmapOutput
    {
        public static async Task Collect()
        {
            var client = new MuseClient();
            var ok = await client.Connect(MyMuse.Address);
            if (ok)
            {
                await client.Subscribe(
                    Channel.EEG_AF7,
                    Channel.EEG_AF8,
                    Channel.EEG_TP10,
                    Channel.EEG_TP9,
                    Channel.EEG_AUX);

                client.NotifyEeg += Client_NotifyEeg;
                await client.Resume();
                await Task.Run(Key);
                await client.Pause();
                await SavePicture("e:/temp/temp.bmp");
            }
        }
        public static void Key()
        {
            while (!Console.KeyAvailable)
            {
                Thread.Sleep(100);
            }
            var key = Console.ReadKey(intercept: true);
        }

        static List<float> data_AF7 = new List<float>();
        static List<float> data_AF8 = new List<float>();
        static List<float> data_TP10 = new List<float>();
        static List<float> data_TP9 = new List<float>();
        static List<float> data_AUX = new List<float>();

        static int n = 0;

        private static void Client_NotifyEeg(Channel c, Encefalogram e)
        {
            if (c == Channel.EEG_AF7) data_AF7.AddRange(e.Samples);
            else if (c == Channel.EEG_AF8) data_AF8.AddRange(e.Samples);
            else if (c == Channel.EEG_TP9) data_TP9.AddRange(e.Samples);
            else if (c == Channel.EEG_TP10) data_TP10.AddRange(e.Samples);
            else if (c == Channel.EEG_AUX) data_AUX.AddRange(e.Samples);

            if (++n % 10 == 0)
            {
                Print.Encefalogram(e);
            }
        }

        public static async Task SavePicture(string filename)
        {
            int count = Math.Max(data_AF7.Count, data_AF8.Count);
            Bitmap b = new Bitmap(count, 800);
            var g = Graphics.FromImage(b);
            Draw(g, data_AF7, Color.LightBlue, -100);
            Draw(g, data_AF8, Color.Green, -50);

            Draw(g, data_TP9, Color.Red, 0);
            Draw(g, data_TP10, Color.Orange, 50);

            Draw(g, data_AUX, Color.Purple, 100);

            await Task.Run(() => b.Save(filename));
        }

        public static void Draw(Graphics graphics, IList<float> data, Color color, int offset)
        {
            Pen pen = new Pen(color);
            int count = data.Count, y;
            int xa = 0, ya = 0;
            bool first = true;
            for (int x = 0; x < count; x++)
            {
                y = (int)data[x] + 0x800;
                y = y / 10;
                y = y + offset;
                if (first)
                {
                    first = false;
                }
                else
                {
                    graphics.DrawLine(pen, xa, ya, x, y);
                }
                xa = x; ya = y;


            }
        }
    }
}
