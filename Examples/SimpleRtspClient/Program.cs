using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using RtspClientSharp;
using RtspClientSharp.RawFrames.Video;
using RtspClientSharp.Rtsp;

namespace SimpleRtspClient
{
    class Program
    {
        static void Main()
        {
            // var serverUri = new Uri("rtsp://127.0.0.1:8554/test");
            var serverUri = new Uri("rtsp://admin:ruixin888888@192.168.0.214/h264/ch1/sub/av_stream");
            var credentials = new NetworkCredential("admin", "ruixin888888");

            // var serverUri = new Uri("rtsp://192.168.1.201/h264");
            // var credentials = new NetworkCredential("admin", "admin");
            var connectionParameters = new ConnectionParameters(serverUri, credentials);
            var cancellationTokenSource = new CancellationTokenSource();

            Task connectTask = ConnectAsync(connectionParameters, cancellationTokenSource.Token);

            Console.WriteLine("Press any key to cancel");
            Console.ReadLine();

            cancellationTokenSource.Cancel();

            Console.WriteLine("Canceling");
            connectTask.Wait(CancellationToken.None);
        }

        private static async Task ConnectAsync(ConnectionParameters connectionParameters, CancellationToken token)
        {
            try
            {
                string now = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string filename = "rtsp_capture_" + now + ".264";
                FileStream fs_v = new(filename, FileMode.Create);

                TimeSpan delay = TimeSpan.FromSeconds(5);
                var fartst = true;
                using (var rtspClient = new RtspClient(connectionParameters))
                {
                    rtspClient.FrameReceived +=
                        (sender, frame) =>
                        {
                            if (frame is RawH264IFrame rawH264IFrame)
                            {
                                if (fartst)
                                {
                                    var array = rawH264IFrame.SpsPpsSegment.ToArray();
                                    var bytes = new byte[] { 0x00, 0x00, 0x00, 0x01 };
                                    fs_v.Write(bytes, 0, bytes.Length);
                                    fs_v.Write(array, 0, array.Length);
                                    fartst = false;
                                }
                            }

                            Console.WriteLine($"New frame {frame.Timestamp}: {frame.GetType().Name}");
                            var nalUnit = frame.FrameSegment.ToArray();
                            // Output some H264 stream information
                            if (nalUnit.Length > 5)
                            {
                                int nal_ref_idc = (nalUnit[4] >> 5) & 0x03;
                                int nal_unit_type = nalUnit[4] & 0x1F;
                                string description;
                                switch (nal_unit_type)
                                {
                                    case 1:
                                        description = "NON IDR NAL";
                                        break;
                                    case 5:
                                        description = "IDR NAL";
                                        break;
                                    case 6:
                                        description = "SEI NAL";
                                        break;
                                    case 7:
                                        description = "SPS NAL";
                                        break;
                                    case 8:
                                        description = "PPS NAL";
                                        break;
                                    case 9:
                                        description = "ACCESS UNIT DELIMITER NAL";
                                        break;
                                    default:
                                        description = "OTHER NAL";
                                        break;
                                }

                                Console.WriteLine("NAL Ref = " + nal_ref_idc + " NAL Type = " + nal_unit_type + " " +
                                                  description);
                            }

                            fs_v.Write(nalUnit, 0, nalUnit.Length);
                        };

                    while (true)
                    {
                        Console.WriteLine("Connecting...");

                        try
                        {
                            await rtspClient.ConnectAsync(token);
                        }
                        catch (OperationCanceledException)
                        {
                            return;
                        }
                        catch (RtspClientException e)
                        {
                            Console.WriteLine(e.ToString());
                            await Task.Delay(delay, token);
                            continue;
                        }

                        Console.WriteLine("Connected.");

                        try
                        {
                            await rtspClient.ReceiveAsync(token);
                        }
                        catch (OperationCanceledException)
                        {
                            return;
                        }
                        catch (RtspClientException e)
                        {
                            Console.WriteLine(e.ToString());
                            await Task.Delay(delay, token);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
        }
    }
}