using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SimpleRtspPlayer.RawFramesDecoding;
using SimpleRtspPlayer.RawFramesDecoding.DecodedFrames;
using PixelFormat = SimpleRtspPlayer.RawFramesDecoding.PixelFormat;

namespace SimpleRtspPlayer.GUI.Views
{
    /// <summary>
    /// Interaction logic for VideoView.xaml
    /// </summary>
    public partial class VideoView : UserControl
    {
        private static readonly System.Windows.Media.Color DefaultFillColor = Colors.Black;
        private static readonly TimeSpan ResizeHandleTimeout = TimeSpan.FromMilliseconds(500);

        private System.Windows.Media.Color _fillColor = DefaultFillColor;
        private WriteableBitmap _writeableBitmap;

        private int _width;
        private int _height;
        private Int32Rect _dirtyRect;
        private TransformParameters _transformParameters;
        private readonly Action<IDecodedVideoFrame> _invalidateAction;

        private Task _handleSizeChangedTask = Task.CompletedTask;
        private CancellationTokenSource _resizeCancellationTokenSource = new CancellationTokenSource();

        public static readonly DependencyProperty VideoSourceProperty = DependencyProperty.Register(nameof(VideoSource),
            typeof(IVideoSource),
            typeof(VideoView),
            new FrameworkPropertyMetadata(OnVideoSourceChanged));

        public static readonly DependencyProperty FillColorProperty = DependencyProperty.Register(nameof(FillColor),
            typeof(System.Windows.Media.Color),
            typeof(VideoView),
            new FrameworkPropertyMetadata(DefaultFillColor, OnFillColorPropertyChanged));

        public IVideoSource VideoSource
        {
            get => (IVideoSource)GetValue(VideoSourceProperty);
            set => SetValue(VideoSourceProperty, value);
        }

        public System.Windows.Media.Color FillColor
        {
            get => (System.Windows.Media.Color)GetValue(FillColorProperty);
            set => SetValue(FillColorProperty, value);
        }

        public VideoView()
        {
            InitializeComponent();
            _invalidateAction = Invalidate;
        }

        /// <summary>
        /// 设置加载信息显示
        /// </summary>
        /// <param name="connectionAddress">连接地址</param>
        public void SetLoadingInfo(string connectionAddress)
        {
            Dispatcher.Invoke(() =>
            {
                // 显示连接信息
                ConnectionInfo.Text = connectionAddress;
                LoadingInfo.Text = "加载中...";
                LoadingInfo.Visibility = Visibility.Visible;
                
                // 确保背景为黑色
                UpdateBackground();
            });
        }

        /// <summary>
        /// 隐藏加载信息
        /// </summary>
        public void HideLoadingInfo()
        {
            Dispatcher.Invoke(() =>
            {
                LoadingInfo.Visibility = Visibility.Collapsed;
            });
        }
        
        /// <summary>
        /// 更新背景为黑色
        /// </summary>
        private void UpdateBackground()
        {
            // 确保有效尺寸
            if (_width > 0 && _height > 0)
            {
                // 重新初始化位图
                ReinitializeBitmap(_width, _height);
            }
        }

        protected override System.Windows.Size MeasureOverride(System.Windows.Size constraint)
        {
            int newWidth = (int)constraint.Width;
            int newHeight = (int)constraint.Height;

            if (_width != newWidth || _height != newHeight)
            {
                _resizeCancellationTokenSource.Cancel();
                _resizeCancellationTokenSource = new CancellationTokenSource();

                _handleSizeChangedTask = _handleSizeChangedTask.ContinueWith(prev =>
                    HandleSizeChangedAsync(newWidth, newHeight, _resizeCancellationTokenSource.Token));
            }

            return base.MeasureOverride(constraint);
        }

        private async Task HandleSizeChangedAsync(int width, int height, CancellationToken token)
        {
            try
            {
                await Task.Delay(ResizeHandleTimeout, token).ConfigureAwait(false);

                Application.Current.Dispatcher.Invoke(() =>
                {
                    ReinitializeBitmap(width, height);
                }, DispatcherPriority.Send, token);
            }
            catch (OperationCanceledException)
            {
            }
        }

        private void ReinitializeBitmap(int width, int height)
        {
            _width = width;
            _height = height;
            _dirtyRect = new Int32Rect(0, 0, width, height);

            _transformParameters = new TransformParameters(RectangleF.Empty,
                    new System.Drawing.Size(_width, _height),
                    ScalingPolicy.Stretch, PixelFormat.Bgra32, ScalingQuality.FastBilinear);

            _writeableBitmap = new WriteableBitmap(
                width,
                height,
                ScreenInfo.DpiX,
                ScreenInfo.DpiY,
                PixelFormats.Pbgra32,
                null);

            RenderOptions.SetBitmapScalingMode(_writeableBitmap, BitmapScalingMode.NearestNeighbor);

            _writeableBitmap.Lock();

            try
            {
                UpdateBackgroundColor(_writeableBitmap.BackBuffer, _writeableBitmap.BackBufferStride);
                _writeableBitmap.AddDirtyRect(_dirtyRect);
            }
            finally
            {
                _writeableBitmap.Unlock();
            }

            VideoImage.Source = _writeableBitmap;
        }

        private static void OnVideoSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var view = (VideoView)d;

            if (e.OldValue is IVideoSource oldVideoSource)
                oldVideoSource.FrameReceived -= view.OnFrameReceived;

            // 立即更新背景为黑色
            view.UpdateBackground();

            if (e.NewValue is IVideoSource newVideoSource)
            {
                // 显示"连接中..."信息
                if (newVideoSource != null)
                {
                    view.LoadingInfo.Text = "连接中...";
                    view.LoadingInfo.Visibility = Visibility.Visible;
                }
                else
                {
                    view.LoadingInfo.Visibility = Visibility.Collapsed;
                }

                newVideoSource.FrameReceived += view.OnFrameReceived;
            }
            else
            {
                // 如果没有视频源，则显示"暂无视频"
                view.LoadingInfo.Text = "暂无视频";
                view.LoadingInfo.Visibility = Visibility.Visible;
                view.ConnectionInfo.Text = string.Empty;
            }
        }

        private void OnFrameReceived(object sender, IDecodedVideoFrame decodedFrame)
        {
            Application.Current.Dispatcher.Invoke(() => 
            {
                // 收到第一帧时隐藏加载信息
                if (LoadingInfo.Visibility == Visibility.Visible)
                {
                    LoadingInfo.Visibility = Visibility.Collapsed;
                }
                
                _invalidateAction(decodedFrame);
            }, DispatcherPriority.Send);
        }

        private void Invalidate(IDecodedVideoFrame decodedVideoFrame)
        {
            if (_width == 0 || _height == 0)
                return;

            _writeableBitmap.Lock();

            try
            {
                decodedVideoFrame.TransformTo(_writeableBitmap.BackBuffer, _writeableBitmap.BackBufferStride, _transformParameters);

                _writeableBitmap.AddDirtyRect(_dirtyRect);
            }
            finally
            {
                _writeableBitmap.Unlock();
            }
        }

        private static void OnFillColorPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var view = (VideoView)d;
            view._fillColor = (System.Windows.Media.Color)e.NewValue;
            
            // 更新背景颜色
            view.UpdateBackground();
        }

        private unsafe void UpdateBackgroundColor(IntPtr backBufferPtr, int backBufferStride)
        {
            byte* pixels = (byte*)backBufferPtr;
            int color = _fillColor.A << 24 | _fillColor.R << 16 | _fillColor.G << 8 | _fillColor.B;

            Debug.Assert(pixels != null, nameof(pixels) + " != null");

            for (int i = 0; i < _height; i++)
            {
                for (int j = 0; j < _width; j++)
                    ((int*)pixels)[j] = color;

                pixels += backBufferStride;
            }
        }
    }
}