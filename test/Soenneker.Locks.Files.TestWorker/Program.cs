using System;
using System.Threading.Tasks;
using Soenneker.Locks.Files;

using FileLockLease lease = await new FileLock(args[0]).Acquire(TimeSpan.FromSeconds(15));
Console.WriteLine("acquired");
await Console.In.ReadLineAsync();
