using System;
using System.Collections.Generic;
using System.Threading;
using RtspClientSharp.RawFrames;
using RtspClientSharp.RawFrames.Video;
using SimpleRtspPlayer.RawFramesDecoding;
using SimpleRtspPlayer.RawFramesDecoding.DecodedFrames;
using SimpleRtspPlayer.RawFramesDecoding.FFmpeg;
using SimpleRtspPlayer.RawFramesReceiving;

namespace SimpleRtspPlayer.GUI
{
    class RealtimeVideoSource : IVideoSource, IDisposable
    {
        private IRawFramesSource _rawFramesSource;
        private volatile bool _isDisposed;
        private int _isDisposedInt; // 用于 Interlocked 操作的整数标志
        private readonly object _syncLock = new object();

        private readonly Dictionary<FFmpegVideoCodecId, FFmpegVideoDecoder> _videoDecodersMap =
            new Dictionary<FFmpegVideoCodecId, FFmpegVideoDecoder>();

        public event EventHandler<IDecodedVideoFrame> FrameReceived;

        // 安全的DecodedVideoFrame包装类，防止访问已释放的资源
        private class SafeDecodedVideoFrame : IDecodedVideoFrame
        {
            private readonly IDecodedVideoFrame _innerFrame;
            private readonly WeakReference _sourceRef;
            private readonly int _sourceHashCode;

            public SafeDecodedVideoFrame(IDecodedVideoFrame innerFrame, RealtimeVideoSource source)
            {
                _innerFrame = innerFrame;
                _sourceRef = new WeakReference(source);
                _sourceHashCode = source.GetHashCode(); // 存储哈希码用于快速比较
            }

            public void TransformTo(IntPtr buffer, int bufferStride, TransformParameters transformParameters)
            {
                // 快速检查是否已释放（通过比较哈希码）
                var source = _sourceRef.Target as RealtimeVideoSource;
                if (source == null || source.GetHashCode() != _sourceHashCode || source._isDisposed)
                    return;

                try
                {
                    lock (source._syncLock)
                    {
                        // 在锁内再次检查
                        if (source._isDisposed)
                            return;
                            
                        _innerFrame.TransformTo(buffer, bufferStride, transformParameters);
                    }
                }
                catch (AccessViolationException)
                {
                    // 捕获并忽略访问违规异常
                }
                catch (ObjectDisposedException)
                {
                    // 捕获并忽略已释放对象异常
                }
                catch (Exception ex)
                {
                    // 记录其他异常但不抛出
                    Console.WriteLine($"Error in TransformTo: {ex.Message}");
                }
            }
        }

        public void SetRawFramesSource(IRawFramesSource rawFramesSource)
        {
            lock (_syncLock)
            {
                if (_rawFramesSource != null)
                {
                    // 先取消事件注册，防止新的帧进入处理队列
                    _rawFramesSource.FrameReceived -= OnFrameReceived;
                    // 然后释放解码器资源
                    DropAllVideoDecoders();
                }
                _rawFramesSource = rawFramesSource;

                if (rawFramesSource == null)
                    return;

                rawFramesSource.FrameReceived += OnFrameReceived;
            }
        }

        public void Dispose()
        {
            // 使用 Interlocked.Exchange 确保只执行一次
            if (Interlocked.Exchange(ref _isDisposedInt, 1) == 1)
                return;  // 如果已释放，立即返回

            // 设置 bool 标志，用于快速检查
            _isDisposed = true;

            lock (_syncLock)
            {
                // 取消事件订阅
                if (_rawFramesSource != null)
                {
                    _rawFramesSource.FrameReceived -= OnFrameReceived;
                    _rawFramesSource = null;
                }
                
                // 释放解码器资源
                DropAllVideoDecoders();
            }
            
            // 给所有正在处理的帧一个机会完成
            Thread.Sleep(50);
        }

        private void DropAllVideoDecoders()
        {
            lock (_syncLock)
            {
                foreach (FFmpegVideoDecoder decoder in _videoDecodersMap.Values)
                {
                    try
                    {
                        decoder.Dispose();
                    }
                    catch (Exception ex)
                    {
                        // 记录但不抛出异常
                        Console.WriteLine($"Error disposing decoder: {ex.Message}");
                    }
                }

                _videoDecodersMap.Clear();
            }
        }

        private void OnFrameReceived(object sender, RawFrame rawFrame)
        {
            // 检查资源是否已释放
            if (_isDisposed || rawFrame == null || !(rawFrame is RawVideoFrame rawVideoFrame))
                return;

            try
            {
                // 在锁外进行类型检查和基本验证，避免不必要的锁争用
                FFmpegVideoDecoder decoder;
                
                lock (_syncLock)
                {
                    if (_isDisposed)
                        return;
                        
                    // 使用原有的 GetDecoderForFrame 方法获取或创建解码器
                    decoder = GetDecoderForFrame(rawVideoFrame);
                }
                
                // 解码器创建失败
                if (decoder == null)
                    return;
                    
                // 尝试解码视频帧
                IDecodedVideoFrame originalFrame = decoder.TryDecode(rawVideoFrame);

                // 解码成功并有订阅者，使用安全包装后再触发事件
                if (originalFrame != null && !_isDisposed && FrameReceived != null)
                {
                    // 使用安全包装类包装原始帧
                    var safeFrame = new SafeDecodedVideoFrame(originalFrame, this);
                    
                    // 再次检查是否已释放
                    if (!_isDisposed)
                    {
                        FrameReceived(this, safeFrame);
                    }
                }
            }
            catch (ObjectDisposedException)
            {
                // 忽略已释放对象的异常
            }
            catch (AccessViolationException)
            {
                // 忽略访问违规异常，这通常是因为访问了已释放的本机资源
            }
            catch (InvalidOperationException)
            {
                // 忽略操作无效的异常，可能是因为解码器状态无效
            }
            catch (Exception ex)
            {
                // 记录其他异常
                Console.WriteLine($"Error in OnFrameReceived: {ex.Message}");
            }
        }

        private FFmpegVideoDecoder GetDecoderForFrame(RawVideoFrame videoFrame)
        {
            // 如果已释放资源，返回null
            if (_isDisposed)
                return null;

            try
            {
                lock (_syncLock)
                {
                    if (_isDisposed)
                        return null;
                        
                    FFmpegVideoCodecId codecId = DetectCodecId(videoFrame);
                    if (!_videoDecodersMap.TryGetValue(codecId, out FFmpegVideoDecoder decoder))
                    {
                        decoder = FFmpegVideoDecoder.CreateDecoder(codecId);
                        _videoDecodersMap.Add(codecId, decoder);
                    }

                    return decoder;
                }
            }
            catch (Exception ex)
            {
                // 任何解码器获取过程中的异常都记录并返回null
                Console.WriteLine($"Error getting decoder: {ex.Message}");
                return null;
            }
        }

        private FFmpegVideoCodecId DetectCodecId(RawVideoFrame videoFrame)
        {
            if (videoFrame is RawJpegFrame)
                return FFmpegVideoCodecId.MJPEG;
            if (videoFrame is RawH264Frame)
                return FFmpegVideoCodecId.H264;

            throw new ArgumentOutOfRangeException(nameof(videoFrame));
        }
    }
}