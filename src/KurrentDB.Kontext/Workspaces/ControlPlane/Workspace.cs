// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Eventuous;
using Jint;
using KurrentDB.Scripting;

namespace KurrentDB.Kontext.Workspaces.ControlPlane;

public sealed class Workspace : Aggregate<WorkspaceState> {
	static readonly Engine ValidateEngine = JintEngineFactory.CreateEngine();

	public void Create(CreateWorkspaceRequest cmd) {
		ValidateName(cmd.Name);
		ValidateRules(cmd.Name, cmd.FilterRules);

		switch (State.Lifecycle) {
			case WorkspaceLifecycle.Unspecified:
			case WorkspaceLifecycle.Deleted:
				Apply(new WorkspaceCreated(
					cmd.Name, cmd.FilterRules,
					cmd.FullTextIndexingEnabled, cmd.SemanticIndexingEnabled,
					cmd.DisableMemory, cmd.DisableImports, cmd.DisableInquiries, cmd.ReadOnly,
					DateTime.UtcNow));
				return;
			case WorkspaceLifecycle.Created:
			case WorkspaceLifecycle.Started:
			case WorkspaceLifecycle.Stopped:
				// Idempotent only when rules and flags all match
				if (!RulesEqual(State.FilterRules, cmd.FilterRules)
					|| State.FullTextIndexingEnabled != cmd.FullTextIndexingEnabled
					|| State.SemanticIndexingEnabled != cmd.SemanticIndexingEnabled
					|| State.DisableMemory != cmd.DisableMemory
					|| State.DisableImports != cmd.DisableImports
					|| State.DisableInquiries != cmd.DisableInquiries
					|| State.ReadOnly != cmd.ReadOnly)
					throw new WorkspaceAlreadyExistsException(cmd.Name);
				return;
		}
	}

	public void Start() {
		switch (State.Lifecycle) {
			case WorkspaceLifecycle.Created:
			case WorkspaceLifecycle.Stopped:
				Apply(new WorkspaceStarted(State.Id.Name, DateTime.UtcNow));
				return;
			case WorkspaceLifecycle.Started:
				return; // idempotent
			case WorkspaceLifecycle.Unspecified:
			case WorkspaceLifecycle.Deleted:
				throw new WorkspaceNotFoundException(State.Id.Name);
		}
	}

	public void Stop() {
		switch (State.Lifecycle) {
			case WorkspaceLifecycle.Started:
				Apply(new WorkspaceStopped(State.Id.Name, DateTime.UtcNow));
				return;
			case WorkspaceLifecycle.Created:
			case WorkspaceLifecycle.Stopped:
				return; // idempotent
			case WorkspaceLifecycle.Unspecified:
			case WorkspaceLifecycle.Deleted:
				throw new WorkspaceNotFoundException(State.Id.Name);
		}
	}

	public void Delete() {
		switch (State.Lifecycle) {
			case WorkspaceLifecycle.Unspecified:
				throw new WorkspaceNotFoundException(State.Id.Name);
			case WorkspaceLifecycle.Deleted:
				return; // idempotent
			case WorkspaceLifecycle.Started:
				throw new WorkspaceInvalidStateException(State.Id.Name, State.Lifecycle, "be deleted");
			case WorkspaceLifecycle.Created:
			case WorkspaceLifecycle.Stopped:
				Apply(new WorkspaceDeleted(State.Id.Name, DateTime.UtcNow));
				return;
		}
	}

	static void ValidateName(string name) {
		if (string.IsNullOrWhiteSpace(name))
			throw new WorkspaceInvalidException("Workspace name must not be empty.");

		foreach (var c in name)
			if (!char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_'))
				throw new WorkspaceInvalidException(
					$"Workspace name '{name}' is invalid. Allowed characters: [A-Za-z0-9_-].");
	}

	static void ValidateRules(string name, IReadOnlyList<FilterRule> rules) {
		if (rules is null || rules.Count == 0)
			throw new WorkspaceInvalidException("At least one filter rule is required.");

		if (WorkspaceNaming.IsDefault(name)) {
			if (rules.Count != 1 || rules[0].StreamPrefix.Length != 0 || rules[0].Filter is not null)
				throw new WorkspaceInvalidException(
					$"Invalid filter rules for the '{WorkspaceNaming.DefaultName}' workspace.");
			return;
		}

		foreach (var rule in rules) {
			if (rule.StreamPrefix is null)
				throw new WorkspaceInvalidException("Filter rule stream prefix must not be null.");

			if (string.IsNullOrWhiteSpace(rule.Filter))
				continue;
			try {
				lock (ValidateEngine)
					JsRecordEvaluator.Compile(ValidateEngine, rule.Filter);
			} catch (Exception ex) {
				throw new WorkspaceInvalidException(
					$"Filter for prefix '{rule.StreamPrefix}' failed to compile: {ex.Message}");
			}
		}
	}

	static bool RulesEqual(IReadOnlyList<FilterRule> a, IReadOnlyList<FilterRule> b) {
		if (a.Count != b.Count)
			return false;
		for (var i = 0; i < a.Count; i++)
			if (!a[i].Equals(b[i]))
				return false;
		return true;
	}
}
