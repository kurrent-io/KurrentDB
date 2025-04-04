// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace EventStore.Core.TransactionLog.Unbuffered;

internal static unsafe class WinNative {
	[DllImport("KERNEL32", SetLastError = true, CharSet = CharSet.Auto, BestFitMapping = false)]
	public static extern bool GetDiskFreeSpace(string path,
		out uint sectorsPerCluster,
		out uint bytesPerSector,
		out uint numberOfFreeClusters,
		out uint totalNumberOfClusters);

	[DllImport("kernel32.dll", SetLastError = true)]
	internal static extern bool WriteFile(
		SafeFileHandle hFile,
		byte* aBuffer,
		UInt32 cbToWrite,
		ref int cbThatWereWritten,
		IntPtr pOverlapped);

	[DllImport("kernel32", SetLastError = true)]
	public static extern bool ReadFile
	(
		SafeFileHandle hFile,
		byte* pBuffer,
		int numberOfBytesToRead,
		ref int pNumberOfBytesRead,
		int overlapped
	);

	[DllImport("kernel32.dll")]
	public static extern bool GetFileSizeEx(SafeFileHandle hFile, out long lpFileSize);

	[DllImport("kernel32.dll", SetLastError = true)]
	internal static extern UInt32 SetFilePointer(
		SafeFileHandle hFile,
		Int32 cbDistanceToMove,
		IntPtr pDistanceToMoveHigh,
		EMoveMethod fMoveMethod);

	[DllImport("KERNEL32", SetLastError = true, CharSet = CharSet.Auto, BestFitMapping = false)]
	public static extern SafeFileHandle CreateFile(String fileName,
		FileAccess desiredAccess,
		FileShare shareMode,
		IntPtr securityAttrs,
		FileMode creationDisposition,
		int flagsAndAttributes,
		IntPtr templateFile);

	public enum EMoveMethod : uint {
		Begin = 0,
		Current = 1,
		End = 2
	}

	[DllImport("kernel32.dll", SetLastError = true)]
	internal static extern bool SetEndOfFile(
		SafeFileHandle hFile);

	[DllImport("kernel32.dll", SetLastError = true)]
	internal static extern bool FlushFileBuffers(SafeFileHandle filehandle);

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool CloseHandle(IntPtr hObject);

	[DllImport("Kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
	public static extern int SetFilePointer(SafeFileHandle handle, int lDistanceToMove,
		out int lpDistanceToMoveHigh, EMoveMethod dwMoveMethod);

	// ReSharper disable once InconsistentNaming
	public const int INVALID_SET_FILE_POINTER = -1;
}
