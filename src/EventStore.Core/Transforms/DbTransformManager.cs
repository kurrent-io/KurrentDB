// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using EventStore.Core.Transforms.Identity;
using EventStore.Plugins.Transforms;
using Serilog;

namespace EventStore.Core.Transforms;

public class DbTransformManager {
	private IReadOnlyList<IDbTransform> _transforms;
	private IDbTransform _activeTransform;

	private IDbTransform FindTransform(TransformType type) {
		if (TryFindTransform(type, out var transform))
			return transform;

		throw new Exception($"Failed to load transform: {type}");
	}

	private bool TryFindTransform(TransformType type, out IDbTransform dbTransform) {
		dbTransform = _transforms?.FirstOrDefault(t => t.Type == type);
		return dbTransform != null;
	}

	private bool TryFindTransform(string name, out IDbTransform dbTransform) {
		dbTransform = _transforms?.FirstOrDefault(t => t.Name == name);
		return dbTransform != null;
	}

	public IChunkTransformFactory GetFactoryForNewChunk() => _activeTransform?.ChunkFactory ??
															 throw new Exception("Active transform not set");

	public IChunkTransformFactory GetFactoryForExistingChunk(TransformType type) => FindTransform(type).ChunkFactory;

	public void LoadTransforms(IReadOnlyList<IDbTransform> transforms) {
		_transforms = transforms;
		Log.Information($"Loaded the following transforms: {string.Join(", ", transforms.Select(t => t.Type))}");

		// the identity transform is always required
		_ = FindTransform(TransformType.Identity);
	}

	public void SetActiveTransform(TransformType type) {
		Log.Information($"Setting the active transform to: {type}");
		_activeTransform = FindTransform(type);
	}

	public bool TrySetActiveTransform(string name) {
		if (!TryFindTransform(name, out var transform))
			return false;

		_activeTransform = transform;
		Log.Information($"Active transform set to: {_activeTransform.Type}");
		return true;
	}

	public bool SupportsTransform(TransformType type) => TryFindTransform(type, out _);

	public static DbTransformManager Default {
		get {
			var dbTransformManager = new DbTransformManager();
			var identityDbTransform = new IdentityDbTransform();
			dbTransformManager.LoadTransforms(new[] { identityDbTransform });
			dbTransformManager.SetActiveTransform(TransformType.Identity);
			return dbTransformManager;
		}
	}
}
