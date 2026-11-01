// Region metadata only. The process handle has QUERY_INFORMATION (0x0400),
// not VM_READ/VM_WRITE/VM_OPERATION. No memory bytes or module paths are read.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace PrivacyMask.Diagnostics;

public static class RegionQuery
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryInfo
    {
        public IntPtr BaseAddress, AllocationBase;
        public uint AllocationProtect;
        public ushort PartitionId;
        public UIntPtr RegionSize;
        public uint State, Protect, Type;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct SystemInfo
    {
        public ushort Architecture, Reserved;
        public uint PageSize;
        public IntPtr MinimumAddress, MaximumAddress;
        public UIntPtr ProcessorMask;
        public uint ProcessorCount, ProcessorType, AllocationGranularity;
        public ushort ProcessorLevel, ProcessorRevision;
    }
    [DllImport("kernel32.dll", SetLastError=true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError=true)]
    private static extern UIntPtr VirtualQueryEx(IntPtr process, IntPtr address, out MemoryInfo info, UIntPtr length);
    [DllImport("kernel32.dll")]
    private static extern void GetNativeSystemInfo(out SystemInfo info);
    [DllImport("kernel32.dll", SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWow64Process(IntPtr process, out bool wow64);
    [DllImport("kernel32.dll")]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    public sealed class Category
    {
        public string Kind { get; set; } = "";
        public double CommittedVirtualMiB { get; set; }
        public int RegionCount { get; set; }
    }
    public sealed class Allocation
    {
        public double CommittedVirtualMiB { get; set; }
        public int RegionCount { get; set; }
        public bool ContainsExecutableRegions { get; set; }
    }
    public sealed class Result
    {
        public bool AddressEnumerationCompleted { get; set; }
        public int RegionQueries { get; set; }
        public double ElapsedMilliseconds { get; set; }
        public double ReservedVirtualMiBNotRam { get; set; }
        public Category[] Categories { get; set; } = Array.Empty<Category>();
        public Allocation[] LargestPrivateAllocations { get; set; } = Array.Empty<Allocation>();
    }

    public static Result Capture(int pid)
    {
        if (pid <= 0 || !Environment.Is64BitProcess || Marshal.SizeOf<MemoryInfo>() != 48)
            throw new InvalidOperationException("Requires a valid PID and 64-bit PowerShell.");
        var handle = OpenProcess(0x0400, false, pid);
        if (handle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            if (!IsWow64Process(handle, out var wow64)) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (wow64) throw new InvalidOperationException("Expected the 64-bit App.");
            GetNativeSystemInfo(out var system);
            var maximum = unchecked((ulong)system.MaximumAddress.ToInt64());
            var timer = Stopwatch.StartNew();
            var categories = new Dictionary<string,Category>();
            var allocations = new Dictionary<ulong,Allocation>();
            ulong cursor=0, reserved=0;
            int queries=0;
            while (cursor <= maximum)
            {
                if (++queries > 1000000 || timer.Elapsed.TotalSeconds > 10)
                    throw new InvalidOperationException("Region query deadline exceeded; incomplete snapshot.");
                var bytes = VirtualQueryEx(handle, unchecked((IntPtr)(long)cursor), out var region, (UIntPtr)48);
                if (bytes.ToUInt64() != 48) throw new Win32Exception(Marshal.GetLastWin32Error());
                var size=region.RegionSize.ToUInt64();
                var start=unchecked((ulong)region.BaseAddress.ToInt64());
                if (size==0 || start > cursor || size > ulong.MaxValue-start || start+size <= cursor)
                    throw new InvalidOperationException("Invalid region progress; incomplete snapshot.");
                if (region.State==0x1000) // MEM_COMMIT
                {
                    var executable = (region.Protect & 0xf0)!=0;
                    var kind = region.Type switch
                    {
                        0x20000 => executable ? "Private executable" : "Private non-executable",
                        0x40000 => "Data mappings",
                        0x1000000 => "Image mappings",
                        _ => "Other committed"
                    };
                    if (!categories.TryGetValue(kind,out var category))
                        categories.Add(kind,category=new Category { Kind=kind });
                    category.CommittedVirtualMiB += size/1048576d;
                    category.RegionCount++;
                    if (region.Type==0x20000)
                    {
                        var allocationBase=unchecked((ulong)region.AllocationBase.ToInt64());
                        if (!allocations.TryGetValue(allocationBase,out var allocation))
                            allocations.Add(allocationBase,allocation=new Allocation());
                        allocation.CommittedVirtualMiB += size/1048576d;
                        allocation.RegionCount++;
                        allocation.ContainsExecutableRegions |= executable;
                    }
                }
                else if (region.State==0x2000) reserved+=size; // MEM_RESERVE, not committed RAM
                cursor=start+size;
            }
            return new Result
            {
                AddressEnumerationCompleted=true, RegionQueries=queries,
                ElapsedMilliseconds=Math.Round(timer.Elapsed.TotalMilliseconds,3),
                ReservedVirtualMiBNotRam=Math.Round(reserved/1048576d,3),
                Categories=categories.Values.OrderByDescending(x=>x.CommittedVirtualMiB).ToArray(),
                LargestPrivateAllocations=allocations.Values.OrderByDescending(x=>x.CommittedVirtualMiB).Take(20).ToArray()
            };
        }
        finally { CloseHandle(handle); }
    }
}
