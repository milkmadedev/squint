// Squint - copied-link safety checker for Windows.
// Copyright (C) 2026 milkmade
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Runtime.InteropServices;

namespace Squint;

internal static partial class NativeMethods
{
    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW")]
    internal static partial int GetWindowLong(IntPtr hwnd, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongW")]
    internal static partial int SetWindowLong(IntPtr hwnd, int index, int newLong);
}
