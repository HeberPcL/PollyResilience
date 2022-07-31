using Polly;
using Polly.Timeout;

namespace PollyTest
{
    class Program
    {
        static void Main(string[] args)
        {
            var cancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = cancellationTokenSource.Token;

            Console.WriteLine("This is an example of use of the Polly policies.");
            Console.WriteLine("Here we will combine the Retry and the TimeOut policies.");
            Console.WriteLine("\r\nPress 'c' to cancel operation...");
            Task.Factory.StartNew(async () => await ExecuteTask(cancellationToken));

            //Request cancellation from the UI thread. 
            char ch = Console.ReadKey().KeyChar;
            if (ch == 'c' || ch == 'C')
            {
                cancellationTokenSource.Cancel();
                Console.WriteLine("\nTask cancellation requested.");
            }

            Console.WriteLine("\r\nEnd of program, press any key...");
            Console.ReadKey();
        }

        private static async Task ExecuteTask(CancellationToken cancellationToken)
        {
            var maxRetryAttempts = 10;
            var pauseBetweenFailures = TimeSpan.FromSeconds(2);
            var timeoutInSec = 30;

            //Retry Policy
            var retryPolicy = Policy
                .Handle<Exception>()
                //.Or<AnyOtherException>()
                .WaitAndRetryAsync(
                    maxRetryAttempts,
                    i => pauseBetweenFailures,
                    (exception, timeSpan, retryCount, context) => ManageRetryException(exception, timeSpan, retryCount, context));

            //TimeOut Policy
            var timeOutPolicy = Policy
                .TimeoutAsync(
                    timeoutInSec,
                    TimeoutStrategy.Pessimistic,
                    (context, timeSpan, task) => ManageTimeoutException(context, timeSpan, task));

            //Combine the two (or more) policies
            var policyWrap = Policy.WrapAsync(retryPolicy, timeOutPolicy);

            //Execute the transient task(s)
            await policyWrap.ExecuteAsync(async (ct) =>
            {
                Console.WriteLine("\r\nExecuting task...");
                var result = await FailedOperation(ct);
            }, new Dictionary<string, object>() { { "ExecuteOperation", "Operation description..." } }, cancellationToken);

            return;
        }
        private static async Task<bool> FailedOperation(CancellationToken ct)
        {
            //Was cancellation already requested? 
            if (ct.IsCancellationRequested == true)
            {
                Console.WriteLine("Task was cancelled before it got started.");
                ct.ThrowIfCancellationRequested();
            }

            //Simulate a too long operation to test the Timeout Exception
            //await Task.Delay(35000);

            //Cancel the operation if requested
            if (ct.IsCancellationRequested == true)
            {
                Console.WriteLine("Task was cancelled !");
                ct.ThrowIfCancellationRequested();
            }

            //Throw an error to see the retry mechanism
            throw new Exception("A fake error occured...");
            return true;
        }

        private static void ManageRetryException(Exception exception, TimeSpan timeSpan, int retryCount, Context context)
        {
            var action = context != null ? context.First().Key : "unknown method";
            var actionDescription = context != null ? context.First().Value : "unknown description";
            var msg = $"Retry n°{retryCount} of {action} ({actionDescription}) : {exception.Message}";
            Console.WriteLine(msg);
        }

        private static Task ManageTimeoutException(Context context, TimeSpan timeSpan, Task task)
        {
            var action = context != null ? context.First().Key : "unknown method";
            var actionDescription = context != null ? context.First().Value : "unknown description";

            task.ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    var msg = $"Running {action} ({actionDescription}) but the execution timed out after {timeSpan.TotalSeconds} seconds, eventually terminated with: {t.Exception}.";
                    Console.WriteLine(msg);
                }
                else if (t.IsCanceled)
                {
                    var msg = $"Running {action} ({actionDescription}) but the execution timed out after {timeSpan.TotalSeconds} seconds, task cancelled.";
                    Console.WriteLine(msg);
                }
            });

            return task;
        }
    }
}