﻿using System;
using System.Collections.Generic;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace Harthoorn.MuseClient
{
    public static partial class Print
    {

        public static string BufferLine(this IBuffer buffer)
        {
            if (buffer is null) return null;

            var bytes = buffer.ToArray();
            var hex = BitConverter.ToString(bytes);
            var parsed = Encoding.ASCII.GetString(bytes);
            parsed = parsed.Replace("\n", "");

            return $"{hex} {parsed}";
        }

        public static string Raw(this IBuffer buffer)
        {

            var dataReader = DataReader.FromBuffer(buffer);
            var output = dataReader.ReadString(buffer.Length);
            output = output.Replace('\n', '.');
            return output;
        }

        public static void Telemetry(Telemetry t)
        {
            Console.WriteLine($"#{t.SequenceId:00000000} Telemetry    : {t.BatteryLevel:0.0}% battery, {t.Voltage / 1000:0.0}V, {t.Temperature}°C");
        }

        public static void Accelerometer(Accelerometer a, bool full = false)
        {
            if (!full)
            {
                Console.WriteLine($"#{a.SequenceId:00000000} Accelerometer: {AcceleroSample(a.Samples[0])}");
            }
            else
            {
                Console.WriteLine($" - Sequence: #{a.SequenceId}");

                foreach (var sample in a.Samples)
                    Console.WriteLine($" - Sample: {AcceleroSample(sample)}");
            }
        }

        public static void Gyroscope(Gyroscope a, bool full = false)
        {
            if (!full)
            {
                Console.WriteLine($"#{a.SequenceId:00000000} Gyroscope     : {GyroSample(a.Samples[0])}");
            }
            else
            {
                Console.WriteLine($" - Sequence: #{a.SequenceId}");

                foreach (var sample in a.Samples)
                    Console.WriteLine($" - Sample: {GyroSample(sample)}");
            }
        }

        public static string AcceleroSample(Vector vector)
        {
            return string.Format("X = {0,5:#}, Y = {1,5:#}, Z = {2,5:#}", vector.X, vector.Y, vector.Z);
        }

        public static string GyroSample(Vector vector)
        {
            return string.Format("X = {0,5:0.0}, Y = {1,5:0.0}, Z = {2,5:0.0}", vector.X, vector.Y, vector.Z);
        }

        static int m = 0;
        public static void Encefalogram(Encefalogram gram)
        {
            //Console.WriteLine("--------------------------------------------");
            m = ++m % 11;
            if (m == 0)
            {
                Console.WriteLine(Floats(gram.Samples));
                //var hex = BitConverter.ToString(gram.Raw);
                //Console.WriteLine(hex);

                //const int amplitude = 0x800, screen = 40;
                //foreach (var sample in gram.Samples)
                //{
                //    float value = (sample * screen / amplitude) + (screen / 2);
                //    int count = (int)value;
                //    var s = new string('*', count);
                //    Console.WriteLine("> " + s);
                //}
            }

        }

        public static string Floats(float[] floats)
        {
            var b = new StringBuilder();
            foreach (var f in floats)
            {
                b.Append(string.Format("{0,-7:#####.##}", f));
                b.Append("  ");
            }
            return b.ToString();
        }

        public static void Device(BluetoothLEDevice device)
        {
            Console.WriteLine($"Name: {device.Name}");
            Console.WriteLine($"Status: {device.ConnectionStatus}");
            Console.WriteLine($"Id: {device.DeviceId}");
            Console.WriteLine($"Can pair: {device.DeviceInformation.Pairing.CanPair}");
            Console.WriteLine($"Paired: {device.DeviceInformation.Pairing.IsPaired}");
            Console.WriteLine($"Kind: {device.DeviceInformation.Kind}");
            Console.WriteLine($"Enabled: {device.DeviceInformation.IsEnabled}");

            Console.WriteLine("Characteristics:");
            foreach (var service in device.GattServices)
            {
                DeviceService(service);
            }
        }

        public static void DeviceService(GattDeviceService service)
        {
            Console.WriteLine($"Service: {service.AttributeHandle} - {service.Uuid}");
            foreach (var characteristic in service.GetAllCharacteristics())
            {
                Characteristic(characteristic);
            }
        }

        public static void Characteristic(GattCharacteristic c)
        {
            Console.WriteLine($" - Characteristic {c.AttributeHandle}: {c.CharacteristicProperties} ({c.ProtectionLevel}) - {c.Uuid}");
            //foreach (var d in c.GetAllDescriptors()) Descriptor(d);
            //PresentationFormats(c.PresentationFormats);
        }

        public static void GattDescriptor(GattDescriptor d)
        {
            Console.WriteLine($"           - Descriptor {d.AttributeHandle}: {d.ProtectionLevel} - {d.Uuid} ");
        }

        public static void GattFormat(IEnumerable<GattPresentationFormat> formats)
        {
            foreach (var format in formats)
            {
                Console.WriteLine($"FORMAT: {format.Description}, {format.FormatType}, {format.Namespace}, {format.Unit}");
            }

        }

        public static void BleManufacturer(IEnumerable<BluetoothLEManufacturerData> sections)
        {
            foreach (var section in sections)
            {
                Console.WriteLine($"company id: {section.CompanyId}");
                var s = section.Data.BufferLine();
                Console.WriteLine($"data: {s}");
            }
        }

        public static void BleAdvertisement(IEnumerable<BluetoothLEAdvertisementDataSection> data)
        {
            int i = 0;
            foreach (var section in data)
            {
                var s = section.Data.BufferLine();
                Console.WriteLine($"{++i} - {s}");
            }
        }
    }

}
