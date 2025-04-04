// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using EventStore.Common.Utils;
#pragma warning disable 1591

namespace KurrentDB.TestClient;

public class ConsoleTable {
	private readonly string[] _header;
	private readonly int[] _columnWidths;
	private readonly List<string> _rows;

	public ConsoleTable(params string[] header) {
		Ensure.NotNull(header, "header");

		_header = header;
		_columnWidths = _header.Select(s => s.Length + 4).ToArray();
		_rows = new List<string>();

		AppendRow(_header);
	}

	public string CreateIndentedTable() {
		return string.Format("{0}{1}{0}", Environment.NewLine, CreateTable());
	}

	public string CreateTable() {
		var lineSeparator = RowSeparator(_columnWidths.Sum() + _columnWidths.Length + 1);

		var table = new StringBuilder();
		foreach (var row in _rows) {
			table.AppendLine(lineSeparator);
			table.AppendLine(row);
		}

		table.Append(lineSeparator);

		return table.ToString();
	}

	private string RowSeparator(int width) {
		return new string('-', width);
	}

	public void AppendRow(params string[] cells) {
		if (cells == null || cells.Length != _header.Length)
			return;

		var row = new StringBuilder();
		for (int i = 0; i < cells.Length; i++) {
			var format = "|{0," + _columnWidths[i] + "}";
			row.AppendFormat(format, cells[i]);
		}

		row.Append("|");

		_rows.Add(row.ToString());
	}
}
