// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable InconsistentNaming
// ReSharper disable CheckNamespace

namespace System.Runtime;

/// <summary>
/// Enum representing the operating system platform.
/// </summary>
public enum RuntimeOSPlatform {
    /// <summary>
    /// Represents an unknown operating system platform.
    /// </summary>
    Unknown,
     
    /// <summary>
    /// Represents the FreeBSD operating system platform.
    /// </summary>
    FreeBSD,
    
    /// <summary>
    /// Represents the Linux operating system platform.
    /// </summary>
    Linux, 

    /// <summary>
    /// Represents the OSX (macOS) operating system platform.
    /// </summary>
    OSX, 
    
    /// <summary>
    /// Represents the Windows operating system platform.
    /// </summary>
    Windows
}
