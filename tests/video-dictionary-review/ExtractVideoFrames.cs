using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Media.Editing;
using Windows.Storage;
using Windows.Storage.Streams;

internal static class ExtractVideoFrames
{
    private static int Main(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("Usage: ExtractVideoFrames <video> <output-dir> <interval-seconds> [start] [end]");
            return 2;
        }

        string videoPath = Path.GetFullPath(args[0]);
        string outputDirectory = Path.GetFullPath(args[1]);
        double interval = Double.Parse(args[2], CultureInfo.InvariantCulture);
        double start = args.Length >= 4 ? Double.Parse(args[3], CultureInfo.InvariantCulture) : 0.0;

        StorageFile source = Wait(StorageFile.GetFileFromPathAsync(videoPath));
        MediaClip clip = Wait(MediaClip.CreateFromFileAsync(source));
        MediaComposition composition = new MediaComposition();
        composition.Clips.Add(clip);

        double duration = composition.Duration.TotalSeconds;
        double end = args.Length >= 5
            ? Math.Min(Double.Parse(args[4], CultureInfo.InvariantCulture), duration)
            : duration;

        Directory.CreateDirectory(outputDirectory);
        StorageFolder folder = Wait(StorageFolder.GetFolderFromPathAsync(outputDirectory));

        Console.WriteLine("duration={0:F3}s size={1}x{2} interval={3:F3}s", duration,
            clip.GetVideoEncodingProperties().Width, clip.GetVideoEncodingProperties().Height, interval);

        for (double second = start; second <= end + 0.0001; second += interval)
        {
            string name = String.Format(CultureInfo.InvariantCulture, "frame-{0:000.0}.jpg", second);
            StorageFile destinationFile = Wait(folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting));

            using (IRandomAccessStreamWithContentType thumbnail = Wait(composition.GetThumbnailAsync(
                TimeSpan.FromSeconds(second), 1280, 624, VideoFramePrecision.NearestFrame)))
            using (IRandomAccessStream destination = Wait(destinationFile.OpenAsync(FileAccessMode.ReadWrite)))
            {
                Wait(RandomAccessStream.CopyAsync(thumbnail, destination));
            }

            Console.WriteLine("{0:F1}\t{1}", second, name);
        }

        return 0;
    }

    private static T Wait<T>(IAsyncOperation<T> operation)
    {
        TaskCompletionSource<T> completion = new TaskCompletionSource<T>();
        operation.Completed = delegate(IAsyncOperation<T> info, AsyncStatus status)
        {
            if (status == AsyncStatus.Completed) completion.TrySetResult(info.GetResults());
            else if (status == AsyncStatus.Canceled) completion.TrySetCanceled();
            else completion.TrySetException(info.ErrorCode ?? new InvalidOperationException("WinRT operation failed."));
        };
        return completion.Task.GetAwaiter().GetResult();
    }

    private static T Wait<T, TProgress>(IAsyncOperationWithProgress<T, TProgress> operation)
    {
        TaskCompletionSource<T> completion = new TaskCompletionSource<T>();
        operation.Completed = delegate(IAsyncOperationWithProgress<T, TProgress> info, AsyncStatus status)
        {
            if (status == AsyncStatus.Completed) completion.TrySetResult(info.GetResults());
            else if (status == AsyncStatus.Canceled) completion.TrySetCanceled();
            else completion.TrySetException(info.ErrorCode ?? new InvalidOperationException("WinRT operation failed."));
        };
        return completion.Task.GetAwaiter().GetResult();
    }
}
