using Microsoft.VisualBasic;
using System;
using System.Collections;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
// Utility to automatically download radio programmes, using a plugin framework for provider specific implementation.
// Copyright © 2007-2010 Matt Robinson
//
// This program is free software; you can redistribute it and/or modify it under the terms of the GNU General
// Public License as published by the Free Software Foundation; either version 2 of the License, or (at your
// option) any later version.
//
// This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the
// implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU General Public
// License for more details.
//
// You should have received a copy of the GNU General Public License along with this program; if not, write
// to the Free Software Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA 02110-1301 USA.


using System.Collections.Generic;
using System.Data.SQLite;
namespace RadioDld
{

	public class SQLiteMonTransaction : IDisposable
	{

		private static Dictionary<SQLiteTransaction, string> readerInfo = new Dictionary<SQLiteTransaction, string>();

		private static object readerInfoLock = new object();
		private bool isDisposed;

		private SQLiteTransaction wrappedTrans;
		public SQLiteMonTransaction(SQLiteTransaction transaction)
		{
			wrappedTrans = transaction;

			StackTrace trace = new StackTrace(true);

			lock (readerInfoLock) {
				readerInfo.Add(wrappedTrans, trace.ToString());
			}
		}

		public static Exception AddTransactionsInfo(Exception exp)
		{
			string info = string.Empty;

			lock (readerInfoLock) {
				foreach (string entry in readerInfo.Values) {
					info += entry + Environment.NewLine;
				}
			}

			if (info != string.Empty) {
				exp.Data.Add("transactions", info);
			}

			return exp;
		}

		public SQLiteTransaction Trans {
			get { return wrappedTrans; }
		}

		protected virtual void Dispose(bool disposing)
		{
			if (!this.isDisposed) {
				if (disposing) {
					lock (readerInfoLock) {
						readerInfo.Remove(wrappedTrans);
					}

					wrappedTrans.Dispose();
				}
			}

			this.isDisposed = true;
		}

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		protected override void Finalize()
		{
			Dispose(false);
			base.Finalize();
		}
	}
}