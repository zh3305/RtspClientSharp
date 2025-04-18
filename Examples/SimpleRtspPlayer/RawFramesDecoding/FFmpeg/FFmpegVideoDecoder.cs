using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using RtspClientSharp.RawFrames.Video;
using SimpleRtspPlayer.RawFramesDecoding.DecodedFrames;

namespace SimpleRtspPlayer.RawFramesDecoding.FFmpeg
{
    class FFmpegVideoDecoder
    {
        private readonly IntPtr _decoderHandle;
        private readonly FFmpegVideoCodecId _videoCodecId;
        private readonly object _decoderLock = new object();

        private DecodedVideoFrameParameters _currentFrameParameters =
            new DecodedVideoFrameParameters(0, 0, FFmpegPixelFormat.None);

        private readonly Dictionary<TransformParameters, FFmpegDecodedVideoScaler> _scalersMap =
            new Dictionary<TransformParameters, FFmpegDecodedVideoScaler>();

        private byte[] _extraData = new byte[0];
        private bool _disposed;

        private FFmpegVideoDecoder(FFmpegVideoCodecId videoCodecId, IntPtr decoderHandle)
        {
            _videoCodecId = videoCodecId;
            _decoderHandle = decoderHandle;
        }

        ~FFmpegVideoDecoder()
        {
            Dispose();
        }

        public static FFmpegVideoDecoder CreateDecoder(FFmpegVideoCodecId videoCodecId)
        {
            int resultCode = FFmpegVideoPInvoke.CreateVideoDecoder(videoCodecId, out IntPtr decoderPtr);

            if (resultCode != 0)
                throw new DecoderException(
                    $"An error occurred while creating video decoder for {videoCodecId} codec, code: {resultCode}");

            return new FFmpegVideoDecoder(videoCodecId, decoderPtr);
        }

        public unsafe IDecodedVideoFrame TryDecode(RawVideoFrame rawVideoFrame)
        {
            if (_disposed)
                return null;

            lock (_decoderLock)
            {
                if (_disposed)
                    return null;

                try
                {
                    UpdateExtraData(rawVideoFrame);

                    byte[] frameBytes = GetFrameBytes(rawVideoFrame);

                    fixed (byte* pFrameBytes = frameBytes)
                    {
                        int frameWidth = 0, frameHeight = 0;
                        var framePixelFormat = FFmpegPixelFormat.None;

                        int resultCode = FFmpegVideoPInvoke.DecodeFrame(_decoderHandle, new IntPtr(pFrameBytes),
                            frameBytes.Length, out frameWidth, out frameHeight, out framePixelFormat);

                        if (resultCode != 0 || _disposed)
                            return null;

                        if (frameWidth == 0 || frameHeight == 0)
                            return null;

                        _currentFrameParameters = new DecodedVideoFrameParameters(frameWidth, frameHeight, framePixelFormat);

                        return new DecodedVideoFrame((buffer, bufferStride, parameters) => TransformTo(buffer, bufferStride, parameters));
                    }
                }
                catch (ObjectDisposedException)
                {
                    return null;
                }
                catch (AccessViolationException)
                {
                    return null;
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        private unsafe void UpdateExtraData(RawVideoFrame rawVideoFrame)
        {
            if (rawVideoFrame is RawH264IFrame rawH264IFrame)
            {
                if (rawH264IFrame.SpsPpsSegment.Array != null &&
                    !_extraData.SequenceEqual(rawH264IFrame.SpsPpsSegment))
                {
                    if (_extraData.Length != rawH264IFrame.SpsPpsSegment.Count)
                        _extraData = new byte[rawH264IFrame.SpsPpsSegment.Count];

                    Buffer.BlockCopy(rawH264IFrame.SpsPpsSegment.Array, rawH264IFrame.SpsPpsSegment.Offset,
                        _extraData, 0, rawH264IFrame.SpsPpsSegment.Count);

                    fixed (byte* initDataPtr = _extraData)
                    {
                        int resultCode = FFmpegVideoPInvoke.SetVideoDecoderExtraData(_decoderHandle,
                            (IntPtr)initDataPtr, _extraData.Length);

                        if (resultCode != 0)
                            throw new DecoderException(
                                $"An error occurred while setting video extra data, {_videoCodecId} codec, code: {resultCode}");
                    }
                }
            }
        }

        private byte[] GetFrameBytes(RawVideoFrame rawVideoFrame)
        {
            // 从原始帧中获取字节数据
            if (rawVideoFrame.FrameSegment.Array == null)
                return Array.Empty<byte>();
                
            byte[] frameBytes = new byte[rawVideoFrame.FrameSegment.Count];
            Buffer.BlockCopy(rawVideoFrame.FrameSegment.Array, rawVideoFrame.FrameSegment.Offset, 
                frameBytes, 0, rawVideoFrame.FrameSegment.Count);
            return frameBytes;
        }

        public void Dispose()
        {
            lock (_decoderLock)
            {
                if (_disposed)
                    return;

                _disposed = true;
                FFmpegVideoPInvoke.RemoveVideoDecoder(_decoderHandle);
                DropAllVideoScalers();
                GC.SuppressFinalize(this);
            }
        }

        private void DropAllVideoScalers()
        {
            foreach (var scaler in _scalersMap.Values)
                scaler.Dispose();

            _scalersMap.Clear();
        }

        private void TransformTo(IntPtr buffer, int bufferStride, TransformParameters parameters)
        {
            if (_disposed)
                return;

            lock (_decoderLock)
            {
                if (_disposed)
                    return;

                try
                {
                    if (!_scalersMap.TryGetValue(parameters, out FFmpegDecodedVideoScaler videoScaler))
                    {
                        videoScaler = FFmpegDecodedVideoScaler.Create(_currentFrameParameters, parameters);
                        _scalersMap.Add(parameters, videoScaler);
                    }

                    int resultCode = FFmpegVideoPInvoke.ScaleDecodedVideoFrame(_decoderHandle, videoScaler.Handle, buffer, bufferStride);

                    if (resultCode != 0)
                        throw new DecoderException($"An error occurred while converting decoding video frame, {_videoCodecId} codec, code: {resultCode}");
                }
                catch (ObjectDisposedException)
                {
                    // 忽略已释放对象异常
                }
                catch (AccessViolationException)
                {
                    // 忽略访问违规异常
                }
            }
        }
    }
}