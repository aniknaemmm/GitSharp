﻿/*
 * Copyright (C) 2009, Google Inc.
 * Copyright (C) 2009, Henon <meinrad.recheis@gmail.com>
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


using System.Collections.Generic;
using GitSharp.Util;
using System.IO;

namespace GitSharp
{
	/// <summary>
	/// Abstraction of arbitrary object storage.
	/// <para>
	/// An object database stores one or more Git objects, indexed by their unique
	/// <see cref="ObjectId"/>. Optionally an object database can reference one or more
	/// alternates; other <see cref="ObjectDatabase"/> instances that are searched in 
	/// addition to the current database.
	/// </para><para>
	/// Databases are usually divided into two halves: a half that is considered to
	/// be fast to search, and a half that is considered to be slow to search. When
	/// alternates are present the fast half is fully searched (recursively through
	/// all alternates) before the slow half is considered.
	/// </para>
	/// </summary>
	public abstract class ObjectDatabase
    {
        /// <summary>
		/// Constant indicating no alternate databases exist.
        /// </summary>
        public static ObjectDatabase[] NoAlternates = { };

        private readonly AtomicReference<ObjectDatabase[]> _alternates;

        /// <summary>
		/// Initialize a new database instance for access.
        /// </summary>
    	protected ObjectDatabase()
        {
            _alternates = new AtomicReference<ObjectDatabase[]>();
        }

		/// <summary>
		/// Gets if this database is already created; If it returns false, the caller
		/// should invoke <see cref="create"/> to create this database location.
		/// </summary>
		/// <returns></returns>
        public virtual bool exists()
        {
            return true;
        }

        /**
         * Initialize a new object database at this location.
         *
         * @
         *             the database could not be created.
         */
        public virtual void create()
        {
            // Assume no action is required.
        }

        /**
         * Close any resources held by this database and its active alternates.
         */
        public virtual void close()
        {
            closeSelf();
            closeAlternates();
        }

        /**
         * Close any resources held by this database only; ignoring alternates.
         * <p>
         * To fully close this database and its referenced alternates, the caller
         * should instead invoke {@link #close()}.
         */
        public virtual void closeSelf()
        {
            // Assume no action is required.
        }

        /** Fully close all loaded alternates and clear the alternate list. */
        public virtual void closeAlternates()
        {
            ObjectDatabase[] alt = _alternates.get();
            if (alt != null)
            {
                _alternates.set(null);
                foreach (ObjectDatabase d in alt)
                {
                    d.close();
                }
            }
        }

        /**
         * Does the requested object exist in this database?
         * <p>
         * Alternates (if present) are searched automatically.
         *
         * @param objectId
         *            identity of the object to test for existence of.
         * @return true if the specified object is stored in this database, or any
         *         of the alternate databases.
         */
        public virtual bool hasObject(AnyObjectId objectId)
        {
            return hasObjectImpl1(objectId) || hasObjectImpl2(objectId.ToString());
        }

        private bool hasObjectImpl1(AnyObjectId objectId)
        {
            if (hasObject1(objectId))
            {
                return true;
            }
            foreach (ObjectDatabase alt in getAlternates())
            {
                if (alt.hasObjectImpl1(objectId))
                {
                    return true;
                }
            }
            return tryAgain1() && hasObject1(objectId);
        }

        private bool hasObjectImpl2(string objectId)
        {
            if (hasObject2(objectId))
            {
                return true;
            }
            foreach (ObjectDatabase alt in getAlternates())
            {
                if (alt.hasObjectImpl2(objectId))
                {
                    return true;
                }
            }
            return false;
        }

        /**
         * Fast half of {@link #hasObject(AnyObjectId)}.
         *
         * @param objectId
         *            identity of the object to test for existence of.
         * @return true if the specified object is stored in this database.
         */
        public abstract bool hasObject1(AnyObjectId objectId);

        /**
         * Slow half of {@link #hasObject(AnyObjectId)}.
         *
         * @param objectName
         *            identity of the object to test for existence of.
         * @return true if the specified object is stored in this database.
         */
        public virtual bool hasObject2(string objectName)
        {
            // Assume the search took place during hasObject1.
            return false;
        }

        /**
         * Open an object from this database.
         * <p>
         * Alternates (if present) are searched automatically.
         *
         * @param curs
         *            temporary working space associated with the calling thread.
         * @param objectId
         *            identity of the object to open.
         * @return a {@link ObjectLoader} for accessing the data of the named
         *         object, or null if the object does not exist.
         * @
         */
        public virtual ObjectLoader openObject(WindowCursor curs, AnyObjectId objectId)
        {
            if (objectId == null) return null;

        	ObjectLoader ldr = OpenObjectImpl1(curs, objectId);
            if (ldr != null)
            {
                return ldr;
            }

            ldr = OpenObjectImpl2(curs, objectId.ToString(), objectId);
            if (ldr != null)
            {
                return ldr;
            }
            return null;
        }

        private ObjectLoader OpenObjectImpl1(WindowCursor curs,
                 AnyObjectId objectId)
        {
        	ObjectLoader ldr = openObject1(curs, objectId);
            if (ldr != null)
            {
                return ldr;
            }

            foreach (ObjectDatabase alt in getAlternates())
            {
                ldr = alt.OpenObjectImpl1(curs, objectId);
                if (ldr != null)
                {
                    return ldr;
                }
            }

            if (tryAgain1())
            {
                ldr = openObject1(curs, objectId);
                if (ldr != null)
                {
                    return ldr;
                }
            }

            return null;
        }

        private ObjectLoader OpenObjectImpl2(WindowCursor curs, string objectName, AnyObjectId objectId)
        {
        	ObjectLoader ldr = openObject2(curs, objectName, objectId);
            if (ldr != null)
            {
                return ldr;
            }

            foreach (ObjectDatabase alt in getAlternates())
            {
                ldr = alt.OpenObjectImpl2(curs, objectName, objectId);
                if (ldr != null)
                {
                    return ldr;
                }
            }

            return null;
        }

        /**
         * Fast half of {@link #openObject(WindowCursor, AnyObjectId)}.
         *
         * @param curs
         *            temporary working space associated with the calling thread.
         * @param objectId
         *            identity of the object to open.
         * @return a {@link ObjectLoader} for accessing the data of the named
         *         object, or null if the object does not exist.
         * @
         */
        public abstract ObjectLoader openObject1(WindowCursor curs,
                AnyObjectId objectId);

        /**
         * Slow half of {@link #openObject(WindowCursor, AnyObjectId)}.
         *
         * @param curs
         *            temporary working space associated with the calling thread.
         * @param objectName
         *            name of the object to open.
         * @param objectId
         *            identity of the object to open.
         * @return a {@link ObjectLoader} for accessing the data of the named
         *         object, or null if the object does not exist.
         * @
         */
        public virtual ObjectLoader openObject2(WindowCursor curs, string objectName,
                AnyObjectId objectId)
        {
            // Assume the search took place during openObject1.
            return null;
        }

		/// <summary>
		/// Open the object from all packs containing it.
		/// <para>
		/// If any alternates are present, their packs are also considered.
		/// </para>
		/// </summary>
		/// <param name="out">
		/// Result collection of loaders for this object, filled with
		/// loaders from all packs containing specified object
		/// </param>
		/// <param name="windowCursor">
		/// Temporary working space associated with the calling thread.
		/// </param>
		/// <param name="objectId"><see cref="ObjectId"/> of object to search for.</param>
        public virtual void OpenObjectInAllPacks(ICollection<PackedObjectLoader> @out, WindowCursor windowCursor, AnyObjectId objectId)
        {
            OpenObjectInAllPacksImplementation(@out, windowCursor, objectId);
            foreach (ObjectDatabase alt in getAlternates())
            {
                alt.OpenObjectInAllPacksImplementation(@out, windowCursor, objectId);
            }
        }

		/// <summary>
		/// Open the object from all packs containing it.
		/// <para>
		/// If any alternates are present, their packs are also considered.
		/// </para>
		/// </summary>
		/// <param name="out">
		/// Result collection of loaders for this object, filled with
		/// loaders from all packs containing specified object.
		/// </param>
		/// <param name="windowCursor">
		/// Temporary working space associated with the calling thread.
		/// </param>
		/// <param name="objectId"><see cref="ObjectId"/> of object to search for.</param>
        public virtual void OpenObjectInAllPacksImplementation(ICollection<PackedObjectLoader> @out, WindowCursor windowCursor, AnyObjectId objectId)
        {
            // Assume no pack support
        }

        /**
         * @return true if the fast-half search should be tried again.
         */
        public virtual bool tryAgain1()
        {
            return false;
        }

        /**
         * Get the alternate databases known to this database.
         *
         * @return the alternate list. Never null, but may be an empty array.
         */
        public virtual ObjectDatabase[] getAlternates()
        {
            ObjectDatabase[] r = _alternates.get();
            if (r == null)
            {
                lock (_alternates)
                {
                    r = _alternates.get();
                    if (r == null)
                    {
                        try
                        {
                            r = loadAlternates();
                        }
                        catch (IOException)
                        {
                            r = NoAlternates;
                        }
                        _alternates.set(r); // [henon] possible deadlock?
                    }
                }
            }
            return r;
        }

        /**
         * Load the list of alternate databases into memory.
         * <p>
         * This method is invoked by {@link #getAlternates()} if the alternate list
         * has not yet been populated, or if {@link #closeAlternates()} has been
         * called on this instance and the alternate list is needed again.
         * <p>
         * If the alternate array is empty, implementors should consider using the
         * constant {@link #NoAlternates}.
         *
         * @return the alternate list for this database.
         * @
         *             the alternate list could not be accessed. The empty alternate
         *             array {@link #NoAlternates} will be assumed by the caller.
         */
        public virtual ObjectDatabase[] loadAlternates()
        {
            return NoAlternates;
        }
    }
}
