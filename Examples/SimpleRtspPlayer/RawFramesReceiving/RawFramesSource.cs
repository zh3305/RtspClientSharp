using System;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using RtspClientSharp;
using RtspClientSharp.RawFrames;
using RtspClientSharp.Rtsp;

namespace SimpleRtspPlayer.RawFramesReceiving
{
    class RawFramesSource : IRawFramesSource
    {
        private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);
        private readonly ConnectionParameters _connectionParameters;
        private Task _workTask = Task.CompletedTask;
        private CancellationTokenSource _cancellationTokenSource;
        private volatile bool _isStopped;

        public EventHandler<RawFrame> FrameReceived { get; set; }
        public EventHandler<string> ConnectionStatusChanged { get; set; }

        public RawFramesSource(ConnectionParameters connectionParameters)
        {
            _connectionParameters =
                connectionParameters ?? throw new ArgumentNullException(nameof(connectionParameters));
        }

        public void Start()
        {
            _isStopped = false;
            
            _cancellationTokenSource = new CancellationTokenSource();

            CancellationToken token = _cancellationTokenSource.Token;

            _workTask = _workTask.ContinueWith(async p =>
            {
                await ReceiveAsync(token);
            }, token);
        }

        public void Stop()
        {
            _isStopped = true;
            
            try 
            {
                _cancellationTokenSource?.Cancel();
            }
            catch (Exception ex) 
            {
                Console.WriteLine($"Exception during cancellation: {ex.Message}");
            }
            
            try
            {
                Task.Delay(100).Wait();
            }
            catch (Exception)
            {
            }
        }

        private async Task ReceiveAsync(CancellationToken token)
        {
            try
            {
                using (var rtspClient = new RtspClient(_connectionParameters))
                {
                    rtspClient.FrameReceived += RtspClientOnFrameReceived;

                    while (!_isStopped && !token.IsCancellationRequested)
                    {
                        OnStatusChanged("Connecting...");

                        try
                        {
                            await rtspClient.ConnectAsync(token);
                        }
                        catch (InvalidCredentialException)
                        {
                            OnStatusChanged("Invalid login and/or password");
                            await Task.Delay(RetryDelay, token);
                            continue;
                        }
                        catch (RtspClientException e)
                        {
                            OnStatusChanged(e.ToString());
                            await Task.Delay(RetryDelay, token);
                            continue;
                        }

                        OnStatusChanged("Receiving frames...");

                        try
                        {
                            await rtspClient.ReceiveAsync(token);
                        }
                        catch (RtspClientException e)
                        {
                            OnStatusChanged(e.ToString());
                            await Task.Delay(RetryDelay, token);
                        }
                    }
                    
                    rtspClient.FrameReceived -= RtspClientOnFrameReceived;
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                OnStatusChanged($"Error in ReceiveAsync: {ex.Message}");
            }
        }

        private void RtspClientOnFrameReceived(object sender, RawFrame rawFrame)
        {
            if (_isStopped)
                return;
                
            try
            {
                FrameReceived?.Invoke(this, rawFrame);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in frame processing: {ex.Message}");
            }
        }

        private void OnStatusChanged(string status)
        {
            try
            {
                ConnectionStatusChanged?.Invoke(this, status);
            }
            catch (Exception)
            {
            }
        }
    }
}