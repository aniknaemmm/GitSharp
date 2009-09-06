/*
 * Copyright (C) 2007, Robin Rosenberg <robin.rosenberg@dewire.com>
 * Copyright (C) 2008, Shawn O. Pearce <spearce@spearce.org>
 * Copyright (C) 2009, Henon <meinrad.recheis@gmail.com>
 * Copyright (C) 2009, Gil Ran <gilrun@gmail.com>
 *
 * All rights reserved.
 *
 * Redistribution and use in source and binary forms, with or
 * without modification, are permitted provided that the following
 * conditions are met:
 *
 * - Redistributions of source code must retain the above copyright
 *   notice, this list of conditions and the following disclaimer.
 *
 * - Redistributions in binary form must reproduce the above
 *   copyright notice, this list of conditions and the following
 *   disclaimer in the documentation and/or other materials provided
 *   with the distribution.
 *
 * - Neither the name of the Git Development Community nor the
 *   names of its contributors may be used to endorse or promote
 *   products derived from this software without specific prior
 *   written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND
 * CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES,
 * INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES
 * OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE
 * ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR
 * CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
 * SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT
 * NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
 * CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT,
 * STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE)
 * ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF
 * ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using GitSharp.Exceptions;
using GitSharp.TreeWalk.Filter;

namespace GitSharp.TreeWalk
{
	/// <summary>
	/// Walks a Git tree (directory) in Git sort order.
	/// <para>
	/// A new iterator instance should be positioned on the first entry, or at eof.
	/// Data for the first entry (if not at eof) should be available immediately.
	/// </para><para>
	/// Implementors must walk a tree in the Git sort order, which has the following
	/// odd sorting:
	/// <list>
	/// <item>A.c</item>
	/// <item>A/c</item>
	/// <item>A0c</item>
	/// </list>
	/// </para><para>
	/// In the second item, <code>A</code> is the name of a subtree and
	/// <code>c</code> is a file within that subtree. The other two items are files
	/// in the root level tree.
	/// </para>
	/// </summary>
	/// <seealso cref="CanonicalTreeParser"/>
	public abstract class AbstractTreeIterator
	{
		/// <summary>
		/// Default size for the <see cref="Path"/> buffer.
		/// </summary>
		public static int DEFAULT_PATH_SIZE = 128;

		/// <summary>
		/// A dummy <see cref="ObjectId"/> buffer that matches the zero <see cref="ObjectId"/>.
		/// </summary>
		protected static readonly byte[] ZeroId = new byte[Constants.OBJECT_ID_LENGTH];
        
		private readonly AbstractTreeIterator _parent;

		/// <summary>
		/// Create a new iterator with no parent.
		/// </summary>
		protected AbstractTreeIterator()
		{
			_parent = null;
			Path = new byte[DEFAULT_PATH_SIZE];
			PathOffset = 0;
		}

		/// <summary>
		/// Create a new iterator with no parent and a prefix.
		/// 
		/// The prefix path supplied is inserted in front of all paths generated by
		/// this iterator. It is intended to be used when an iterator is being
		/// created for a subsection of an overall repository and needs to be
		/// combined with other iterators that are created to run over the entire
		/// repository namespace.
		/// </summary>
		/// <param name="prefix">
		/// position of this iterator in the repository tree. The value
		/// may be null or the empty string to indicate the prefix is the
		/// root of the repository. A trailing slash ('/') is
		/// automatically appended if the prefix does not end in '/'.
		/// </param>
		protected AbstractTreeIterator(string prefix)
			: this(Constants.CHARSET.GetBytes(prefix))
		{
		}

		/// <summary>
		/// Create a new iterator with no parent and a prefix.
		/// 
		/// The prefix path supplied is inserted in front of all paths generated by
		/// this iterator. It is intended to be used when an iterator is being
		/// created for a subsection of an overall repository and needs to be
		/// combined with other iterators that are created to run over the entire
		/// repository namespace.
		/// </summary>
		/// <param name="prefix">
		/// position of this iterator in the repository tree. The value
		/// may be null or the empty array to indicate the prefix is the
		/// root of the repository. A trailing slash ('/') is
		/// automatically appended if the prefix does not end in '/'.
		/// </param>
		protected AbstractTreeIterator(byte[] prefix)
		{
			_parent = null;

			if (prefix != null && prefix.Length > 0)
			{
				PathLen = prefix.Length;
				Path = new byte[Math.Max(DEFAULT_PATH_SIZE, PathLen + 1)];
				Array.Copy(prefix, 0, Path, 0, PathLen);
				if (Path[PathLen - 1] != (byte)'/')
				{
					Path[PathLen++] = (byte)'/';
				}
				PathOffset = PathLen;
			}
			else
			{
				Path = new byte[DEFAULT_PATH_SIZE];
				PathOffset = 0;
			}
		}

		/// <summary>
		/// Create an iterator for a subtree of an existing iterator.
		/// </summary>
		/// <param name="p">parent tree iterator.</param>
		protected AbstractTreeIterator(AbstractTreeIterator p)
		{
			_parent = p;
			Path = p.Path;
			PathOffset = p.PathLen + 1;
			try
			{
				Path[PathOffset - 1] = (byte)'/';
			}
			catch (IndexOutOfRangeException)
			{
				growPath(p.PathLen);
				Path[PathOffset - 1] = (byte)'/';
			}
		}

		/// <summary>
		/// Create an iterator for a subtree of an existing iterator. 
		/// The caller is responsible for setting up the path of the child iterator.
		/// </summary>
		/// <param name="p">parent tree iterator.</param>
		/// <param name="childPath">
		/// Path array to be used by the child iterator. This path must
		/// contain the path from the top of the walk to the first child
		/// and must end with a '/'.
		/// </param>
		/// <param name="childPathOffset">
		/// position within <code>childPath</code> where the child can
		/// insert its data. The value at
		/// <code>childPath[childPathOffset-1]</code> must be '/'.
		/// </param>
		protected AbstractTreeIterator(AbstractTreeIterator p, byte[] childPath, int childPathOffset)
		{
			_parent = p;
			Path = childPath;
			PathOffset = childPathOffset;
		}

		/// <summary>
		/// Grow the _path buffer larger.
		/// </summary>
		/// <param name="len">
		/// Number of live bytes in the path buffer. This many bytes will
		/// be moved into the larger buffer.
		/// </param>
		public void growPath(int len)
		{
			SetPathCapacity(Path.Length << 1, len);
		}

		/// <summary>
		/// Ensure that path is capable to hold at least <paramref name="capacity"/> bytes.
		/// </summary>
		/// <param name="capacity">the amount of bytes to hold</param>
		/// <param name="length">the amount of live bytes in path buffer</param>
		protected void ensurePathCapacity(int capacity, int length)
		{
			if (Path.Length >= capacity) return;

			byte[] oldPath = Path;
			int currentLength = oldPath.Length;
			int newCapacity = currentLength;

			while (newCapacity < capacity && newCapacity > 0)
			{
				newCapacity <<= 1;
			}

			SetPathCapacity(newCapacity, length);
		}

		/// <summary>
		/// Set path buffer capacity to the specified size
		/// </summary>
		/// <param name="capacity">the new size</param>
		/// <param name="length">the amount of bytes to copy</param>
		private void SetPathCapacity(int capacity, int length)
		{
			var oldPath = Path;
			var newPath = new byte[capacity];
			Array.Copy(oldPath, 0, newPath, 0, length);

			for (AbstractTreeIterator p = this; p != null && p.Path == oldPath; p = p._parent)
			{
				p.Path = newPath;
			}
		}

		/// <summary>
		/// Compare the path of this current entry to another iterator's entry.
		/// </summary>
		/// <param name="treeIterator">
		/// The other iterator to compare the path against.
		/// </param>
		/// <returns>
		/// return -1 if this entry sorts first; 0 if the entries are equal; 1 if
		/// <paramref name="treeIterator"/>'s entry sorts first.
		/// </returns>
		public int pathCompare(AbstractTreeIterator treeIterator)
		{
			return pathCompare(treeIterator, treeIterator.Mode);
		}

		/// <summary>
		/// Compare the path of this current entry to another iterator's entry.
		/// </summary>
		/// <param name="treeIterator">
		/// The other iterator to compare the path against.
		/// </param>
		/// <param name="treeIteratorMode">
		/// The other iterator <see cref="FileMode"/> bits.
		/// </param>
		/// <returns>
		/// return -1 if this entry sorts first; 0 if the entries are equal; 1 if
		/// <paramref name="treeIterator"/>'s entry sorts first.
		/// </returns>
		public int pathCompare(AbstractTreeIterator treeIterator, int treeIteratorMode)
		{
			byte[] a = Path;
			byte[] b = treeIterator.Path;
			int aLen = PathLen;
			int bLen = treeIterator.PathLen;

			// Its common when we are a subtree for both parents to match;
			// when this happens everything in _path[0..cPos] is known to
			// be equal and does not require evaluation again.
			//
			int cPos = AlreadyMatch(this, treeIterator);

			for (; cPos < aLen && cPos < bLen; cPos++)
			{
				int cmp = (a[cPos] & 0xff) - (b[cPos] & 0xff);
				if (cmp != 0)
				{
					return cmp;
				}
			}

			if (cPos < aLen)
			{
				return (a[cPos] & 0xff) - LastPathChar(treeIteratorMode);
			}

			if (cPos < bLen)
			{
				return LastPathChar(Mode) - (b[cPos] & 0xff);
			}

			return LastPathChar(Mode) - LastPathChar(treeIteratorMode);
		}

		private static int AlreadyMatch(AbstractTreeIterator a, AbstractTreeIterator b)
		{
			while (true)
			{
				AbstractTreeIterator ap = a._parent;
				AbstractTreeIterator bp = b._parent;

				if (ap == null || bp == null)
				{
					return 0;
				}

				if (ap.Matches == bp.Matches)
				{
					return a.PathOffset;
				}

				a = ap;
				b = bp;
			}
		}

		private static int LastPathChar(int mode)
		{
			return FileMode.Tree == FileMode.FromBits(mode) ? (byte)'/' : (byte)'\0';
		}

		/// <summary>
		/// Check if the current entry of both iterators has the same id.
		/// <para>
		/// This method is faster than <see cref="getEntryObjectId()"/>as it does not
		/// require copying the bytes out of the buffers. A direct {@link #idBuffer}
		/// compare operation is performed.
		/// </para>
		/// </summary>
		/// <param name="otherIterator">the other iterator to test against.</param>
		/// <returns>
		/// true if both iterators have the same object id; false otherwise.
		/// </returns>
		public virtual bool idEqual(AbstractTreeIterator otherIterator)
		{
			return ObjectId.Equals(idBuffer(), idOffset(), otherIterator.idBuffer(), otherIterator.idOffset());
		}

		/// <summary>
		/// Gets the <see cref="ObjectId"/> of the current entry.
		/// </summary>
		/// <returns>The <see cref="ObjectId"/> for the current entry.</returns>
		public virtual ObjectId getEntryObjectId()
		{
			return ObjectId.FromRaw(idBuffer(), idOffset());
		}

		/// <summary>
		/// Gets the <see cref="ObjectId"/> of the current entry.
		/// </summary>
		/// <param name="objectId">buffer to copy the object id into.</param>
		public virtual void getEntryObjectId(MutableObjectId objectId)
		{
			objectId.FromRaw(idBuffer(), idOffset());
		}

		/// <summary>
		/// The file mode of the current entry.
		/// </summary>
		public virtual FileMode EntryFileMode
		{
			get { return FileMode.FromBits(Mode); }
		}

		/// <summary>
		/// The file mode of the current entry as bits.
		/// </summary>
		public int EntryRawMode
		{
			get { return Mode; }
		}

		/// <summary>
		/// Gets the path of the current entry, as a string.
		/// </summary>
		public string EntryPathString
		{
			get { return TreeWalk.pathOf(this); }
		}

		/// <summary>
		/// Get the byte array buffer object IDs must be copied out of.
		/// <para>
		/// The id buffer contains the bytes necessary to construct an <see cref="ObjectId"/> for
		/// the current entry of this iterator. The buffer can be the same buffer for
		/// all entries, or it can be a unique buffer per-entry. Implementations are
		/// encouraged to expose their private buffer whenever possible to reduce
		/// garbage generation and copying costs.
		/// </summary>
		/// <returns>byte array the implementation stores object IDs within.</returns>
		/// <seealso cref="getEntryObjectId()"/>
		public abstract byte[] idBuffer();

		/**
		 * Get the position within {@link #idBuffer()} of this entry's ObjectId.
		 * 
		 * @return offset into the array returned by {@link #idBuffer()} where the
		 *         ObjectId must be copied out of.
		 */
		public abstract int idOffset();

		/**
		 * Create a new iterator for the current entry's subtree.
		 * <p>
		 * The parent reference of the iterator must be <code>this</code>,
		 * otherwise the caller would not be able to exit out of the subtree
		 * iterator correctly and return to continue walking <code>this</code>.
		 * 
		 * @param repo
		 *            repository to load the tree data from.
		 * @return a new parser that walks over the current subtree.
		 * @throws IncorrectObjectTypeException
		 *             the current entry is not actually a tree and cannot be parsed
		 *             as though it were a tree.
		 * @throws IOException
		 *             a loose object or pack file could not be Read.
		 */
		public abstract AbstractTreeIterator createSubtreeIterator(Repository repo);

		/**
		 * Create a new iterator as though the current entry were a subtree.
		 *
		 * @return a new empty tree iterator.
		 */
		public virtual EmptyTreeIterator createEmptyTreeIterator()
		{
			return new EmptyTreeIterator(this);
		}

		/**
		 * Create a new iterator for the current entry's subtree.
		 * <p>
		 * The parent reference of the iterator must be <code>this</code>, otherwise
		 * the caller would not be able to exit out of the subtree iterator
		 * correctly and return to continue walking <code>this</code>.
		 *
		 * @param repo
		 *            repository to load the tree data from.
		 * @param idBuffer
		 *            temporary ObjectId buffer for use by this method.
		 * @param curs
		 *            window cursor to use during repository access.
		 * @return a new parser that walks over the current subtree.
		 * @throws IncorrectObjectTypeException
		 *             the current entry is not actually a tree and cannot be parsed
		 *             as though it were a tree.
		 * @throws IOException
		 *             a loose object or pack file could not be Read.
		 */
		public virtual AbstractTreeIterator createSubtreeIterator(Repository repo, MutableObjectId idBuffer, WindowCursor curs)
		{
			return createSubtreeIterator(repo);
		}

		/**
		 * Is this tree iterator positioned on its first entry?
		 * <p>
		 * An iterator is positioned on the first entry if <code>back(1)</code>
		 * would be an invalid request as there is no entry before the current one.
		 * <p>
		 * An empty iterator (one with no entries) will be
		 * <code>first() &amp;&amp; eof()</code>.
		 *
		 * @return true if the iterator is positioned on the first entry.
		 */
		public abstract bool first();

		/**
		 * Is this tree iterator at its EOF point (no more entries)?
		 * <p>
		 * An iterator is at EOF if there is no current entry.
		 * 
		 * @return true if we have walked all entries and have none left.
		 */
		public abstract bool eof();

		/**
		 * Move to next entry, populating this iterator with the entry data.
		 * <p>
		 * The delta indicates how many moves forward should occur. The most common
		 * delta is 1 to move to the next entry.
		 * <p>
		 * Implementations must populate the following members:
		 * <ul>
		 * <li>{@link #mode}</li>
		 * <li>{@link #_path} (from {@link #_pathOffset} to {@link #_pathLen})</li>
		 * <li>{@link #_pathLen}</li>
		 * </ul>
		 * as well as any implementation dependent information necessary to
		 * accurately return data from {@link #idBuffer()} and {@link #idOffset()}
		 * when demanded.
		 *
		 * @param delta
		 *            number of entries to move the iterator by. Must be a positive,
		 *            non-zero integer.
		 * @throws CorruptObjectException
		 *             the tree is invalid.
		 */
		public abstract void next(int delta);

		/// <summary>
		/// Move to prior entry, populating this iterator with the entry data.
		/// <para>
		/// The delta indicates how many moves backward should occur.  
		/// The most common delta is 1 to move to the prior entry.
		/// </para><para>
		/// Implementations must populate the following members:
		/// <ul>
		/// <li>{@link #mode}</li>
		/// <li>{@link #_path} (from {@link #_pathOffset} to {@link #_pathLen})</li>
		/// <li>{@link #_pathLen}</li>
		/// </ul>
		/// as well as any implementation dependent information necessary to
		/// accurately return data from {@link #idBuffer()} and {@link #idOffset()}
		/// when demanded.
		/// </summary>
		/// <param name="delta">
		/// Number of entries to move the iterator by. Must be a positive,
		/// non-zero integer.
		/// </param>
		public abstract void back(int delta);

		/// <summary>
		/// Advance to the next tree entry, populating this iterator with its data.
		/// <para>
		/// This method behaves like <code>seek(1)</code> but is called by
		/// <see cref="TreeWalk"/> only if a <see cref="TreeFilter"/> was used and 
		/// ruled out the current entry from the results. In such cases this tree 
		/// iterator may perform special behavior.
		/// </para>
		/// </summary>
		public virtual void skip()
		{
			next(1);
		}

		/// <summary>
		/// Indicates to the iterator that no more entries will be Read.
		/// <para>
		/// This is only invoked by TreeWalk when the iteration is aborted early due
		/// to a <see cref="StopWalkException"/> being thrown from
		/// within a TreeFilter.
		/// </summary>
		public virtual void stopWalk()
		{
			// Do nothing by default.  Most iterators do not care.
		}

		/// <summary>
		/// Gets the Length of the name component of the path for the current entry.
		/// </summary>
		/// <returns></returns>
		public int NameLength
		{
			get { return PathLen - PathOffset; }
		}

		/// <summary>
		/// Get the name component of the current entry path into the provided buffer.
		/// </summary>
		/// <param name="buffer">
		/// The buffer to get the name into, it is assumed that buffer can hold the name.
		/// </param>
		/// <param name="offset">
		/// The offset of the name in the <paramref name="buffer"/>
		/// </param>
		/// <seealso cref="NameLength"/>
		public void getName(byte[] buffer, int offset)
		{
			Array.Copy(Path, PathOffset, buffer, offset, PathLen - PathOffset);
		}

		/// <summary>
		/// Iterator for the parent tree; null if we are the root iterator.
		/// </summary>
		public AbstractTreeIterator Parent
		{
			get { return _parent; }
		}

		/// <summary>
		/// The iterator this current entry is path equal to.
		/// </summary>
		public AbstractTreeIterator Matches { get; set; }

		/// <summary>
		/// Number of entries we moved forward to force a D/F conflict match.
		/// </summary>
		/// <seealso cref="NameConflictTreeWalk"/>
		public int MatchShift { get; set; }

		/// <summary>
		/// <see cref="FileMode"/> bits for the current entry.
		/// <para>
		/// A numerical value from FileMode is usually faster for an iterator to
		/// obtain from its data source so this is the preferred representation.
		/// </para>
		/// </summary>
		public int Mode { get; protected set; }

		/// <summary>
		/// Path buffer for the current entry.
		/// <para>
		/// This buffer is pre-allocated at the start of walking and is shared from
		/// parent iterators down into their subtree iterators. The sharing allows
		/// the current entry to always be a full path from the root, while each
		/// subtree only needs to populate the part that is under their control.
		/// </para>
		/// </summary>
		public byte[] Path { get; protected set; }

		/// <summary>
		/// Position within <see cref="Path"/> this iterator starts writing at.
		/// <para>
		/// This is the first offset in <see cref="Path"/> that this iterator must
		/// populate during <see cref="Next"/>. At the root level (when <see cref="Parent"/>
		/// is null) this is 0. For a subtree iterator the index before this position
		/// should have the value '/'.
		/// </para>
		/// </summary>
		public int PathOffset { get; protected set; }

		/// <summary>
		/// Total Length of the current entry's complete _path from the root.
		/// <para>
		/// This is the number of bytes within <see cref="Path"/> that pertain to the
		/// current entry. Values at this index through the end of the array are
		/// garbage and may be randomly populated from prior entries.
		/// </para>
		/// </summary>
		public int PathLen { get; protected set; }
	}
}