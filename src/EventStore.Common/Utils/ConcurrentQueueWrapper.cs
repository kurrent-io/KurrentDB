// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Concurrent;
using System.Threading;

namespace EventStore.Common.Utils;

//Workaround for: https://github.com/dotnet/corefx/issues/29759
//As from mono 5.2 and .NET Core 2.0, ConcurrentQueue.Count has knowingly been made slower (in some cases, O(N)) in favor of faster Enqueue() and Dequeue() operations (See: https://github.com/dotnet/corefx/issues/29759#issuecomment-390435245)
//This wrapper implements a workaround by maintaining the queue count at the expense of a slightly slower Enqueue()/Dequeue()
//Interlocked.Increment/Decrement operations are of the order of 36-90 cycles (similar to a division operation) which is good enough in our case.
public class ConcurrentQueueWrapper<T> : ConcurrentQueue<T> {
	private volatile int _queueCount = 0;

	public new bool IsEmpty {
		get { return _queueCount <= 0; }
	}

	public new int Count {
		get {
			int curCount = _queueCount;
			return curCount < 0 ? 0 : curCount;
		}
	}

	//Note: The count is not updated atomically together with dequeueing.
	//This means that the count will be eventually consistent but it's not an issue since between the Count call and any other operation items may be enqueued or dequeued anyway and the count will no longer be valid.
	public new bool TryDequeue(out T result) {
		var dequeued = base.TryDequeue(out result);
		if (dequeued)
			Interlocked.Decrement(ref _queueCount);
		return dequeued;
	}

	//Note: The count is not updated atomically together with dequeueing.
	//This means that the count will be eventually consistent but it's not an issue since between the Count call and any other operation items may be enqueued or dequeued anyway and the count will no longer be valid.
	public new void Enqueue(T item) {
		base.Enqueue(item);
		Interlocked.Increment(ref _queueCount);
	}
}

/*
//For future reference, the following code can be used to test if the problem is still occuring.
//Running the application should make the Main thread use 100% CPU and is very very slow.
//The code has been tested on Mono 5.2-5.16 (the issue does not occur on earlier versions) and .NET Core 2.0 - .NET Core 3.0 preview

using System;
using System.Threading;
using System.Collections.Concurrent;

public class Program
{
    private static ConcurrentQueue<int> queue = new ConcurrentQueue<int>();
    private static volatile bool exit = false;

    public static void Main(string[] args)
    {
        Thread t = new Thread(AddEventsThread);
        t.Name = "AddEventsThread";
        t.Start();

        Thread.Sleep(5000); //wait for 5 seconds to make sure the AddEventsThread thread has started
        var queueCount = 0L;
        var totalElapsed = 0L;

        Console.WriteLine("Starting to hammer queue.Count.");

        for(int i=0;i<1000000;i++){
            var start = DateTime.UtcNow.Ticks;
            queueCount = queue.Count;
            totalElapsed += DateTime.UtcNow.Ticks - start;
        }

        Console.Out.WriteLine("Total Elapsed: {0}, Queue Count: {1}", totalElapsed, queueCount);
        exit = true;
    }

    public static void AddEventsThread(){
        while(!exit){
            Console.WriteLine("Enqueing");
            for(int i=0;i<50000 && !exit;i++){
                queue.Enqueue(50);
                Thread.Sleep(1);
            }
            Console.WriteLine("Dequeing");
            for(int i=0;i<50000 && !exit;i++){
                int result;
                queue.TryDequeue(out result);
                Thread.Sleep(1);
            }
        }
    }
}
*/
