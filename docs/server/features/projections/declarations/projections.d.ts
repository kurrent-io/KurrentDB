type ProjectionOptions = {
  /** Overrides the default result stream name for `outputState()`, which is `$projections-{projection-name}-result`. */
  resultStreamName?: string;

  /** Configures the projection to include or exclude link to events. */
  $includeLinks: boolean;

  /** Process events by storing a buffer of events ordered by their position in the event log.  */
  reorderEvents: boolean;

  /**
   * When `reorderEvents` is enabled, this value is used to compare the total
   * milliseconds between the first and last events in the buffer and if the
   * value is equal or greater, the events in the buffer are processed.
   * The buffer is an ordered list of events.
   */
  processingLog: number;

  /**
   * When using `partitionBy`, setting this to true causes the projection state
   * to become an array with two elements.
   * `state[0]` will be the partition's state.
   * `state[1]` will be another state object shared across all partitions,
   *  as initialized by `$initShared`.
   */
  bistate: boolean;
};

type State = any;

type Handlers = {
  /** Handler for events with any event type. */
  $any?: (state: State, event: KurrentEvent) => State;

  /** Initializes the projection state. */
  $init?: () => State;

  /** Initializes the shared state object when bistate is enabled. */
  $initShared?: () => State;

  /** Handler called for each deleted stream. Can only be used with `foreachStream`. */
  $deleted: (
    state: State,
    event: null,
    partition: string,
    isSoftDelete: boolean
  ) => State;
} & {
  [eventType: string]: (state: State, event: KurrentEvent) => State;
};

type EventBody = Record<string, any>;
type EventMetadata = Record<string, any>;

type KurrentEvent = {
  streamId: string;

  eventType: string;

  /**
   * Return value of the partition function when using partitionBy.
   * When partitionBy is not being used, the partition will be an empty string.
   */
  partition: string;

  /** Event data. Synonymous with event.body. Only populated when the event data is JSON. */
  data?: EventBody;

  /** Event data. Synonymous with event.data. Only populated when the event data is JSON. */
  body?: EventBody;

  /** JSON string of event data. */
  bodyRaw: string;

  /** Event metadata as a JS object. */
  metadata?: EventMetadata;

  /** JSON string of event metadata. */
  metadataRaw: string;

  /** When processing LinkTo events, this field stores the metadata
   * of the linkTo event while event.metadata stores the
   * linked event's metadata */
  linkMetadata?: string;

  /** LinkTo event's metadata as a JSON string. */
  linkMetadataRaw?: string;

  /** True when the event has a JSON body. if isJson is false, the event may have an undefined body. */
  isJson: boolean;

  /** Number of the event within its stream, a.k.a. the stream revision or version. */
  sequenceNumber: number;
};

type WhenFn = (handlers: Handlers) => WhenChain;
type ForeachStreamFn = () => PartitionByChain;
type PartitionByFn = (partitionKeyFn: (s: State) => string) => PartitionByChain;
type OutputStateFn = () => OutputStateChain;

type TransformByFn = (transformFn: (s: State) => State) => TransformationChain;
type FilterByFn = (filterFn: (s: State) => boolean) => TransformationChain;

type OutputToFn = (outputStreamName: string) => void;

type ChainWithTransforms = {
  /** Transforms the projection state according to the function provided.  */
  transformBy: TransformByFn;

  /**
   * Filters the projection state according to the function provided.
   * If the filter function returns false, then the projection state will be transformed to null.
   * If the filter function returns true, then the projection state will remain unchanged.
   */
  filterBy: FilterByFn;
};

type ChainWithOutputTo = {
  /**
   * Outputs the projection state to the specified stream.
   *
   * In the case of partitioned state, each state can be output to a different stream depending on the supplied template string.
   * If the projection is running in `Continuous` mode, the projection will create a Result event in the specified stream for each input event.
   * If the projection is running in `OneTime` mode, the projection will create a single Result event in the specified stream with the final state of the projection.
   */
  outputTo: OutputToFn;
};

type ChainWithOutputState = {
  /**
   * Causes a stream called `$projections-{projection-name}-result` to be produced with the state as the event body.
   *
   * If the projection is running in `Continuous` mode, the projection will create a Result event in the `$projections-{projection-name}-result` stream for each input event.
   * If the projection is running in `OneTime` mode, the projection will create a single Result event in the `$projections-{projection-name}-result` stream with the final state of the projection.
   */
  outputState: OutputStateFn;
};

type ChainWithWhen = {
  /** Performs a fold operation across the events in the projection: each event is processed according to the specified handlers. */
  when: WhenFn;
};

type ChainWithPartitionBy = {
  /** Partitions the projection according to the specified partition function. The state received by `when` handlers will be a partition based on this function. */
  partitionBy: PartitionByFn;
};

type ChainWithForeachStream = {
  /** Runs a projection pipeline (with separate state) for each input stream. */
  foreachStream: ForeachStreamFn;
};

type WhenChain = ChainWithTransforms & ChainWithOutputTo & ChainWithOutputState;

type PartitionByChain = ChainWithWhen;

type OutputStateChain = ChainWithTransforms & ChainWithOutputTo;

type TransformationChain = ChainWithTransforms &
  ChainWithOutputTo &
  ChainWithOutputState;

type FromStreamChain = ChainWithWhen &
  ChainWithPartitionBy &
  ChainWithOutputState;

type FromAllChain = FromStreamChain & ChainWithForeachStream;

type FromCategoryChain = FromStreamChain & ChainWithForeachStream;

/**
 * Sets projection options.
 *
 * @param projectionOptions Object containing projection options (see {@link ProjectionOptions})
 */
declare function options(projectionOptions: ProjectionOptions): void;

/**
 * Selects events from a single stream.
 *
 * @param streamId - Specifies the stream to select events from
 */
declare function fromStream(streamId: string): FromStreamChain;

/**
 * Selects events from multiple streams.
 *
 * @param streamIds - Specifies the streams to select events from
 */
declare function fromStreams(streamIds: string[]): FromStreamChain;

/**
 * Selects events from the $all stream.
 */
declare function fromAll(): FromCategoryChain;

/**
 * Selects events from the `$ce-{category}` stream created by the $by_category system projection.
 *
 * @param streamIds - Specifies the category to select events from
 */
declare function fromCategory(category: string): FromCategoryChain;

/**
 * Appends an event to a stream. Can only be used within the handler of {@link when}.
 *
 * @param streamId - Specifies the stream to which events will be emitted.
 * @param eventType - Type of the emitted event.
 * @param eventBody - A JavaScript object representing the JSON body of the emitted event.
 * @param metadata - A JavaScript object representing the JSON metadata of the emitted event.
 */
declare function emit(
  streamId: string,
  eventType: string,
  eventBody: EventBody,
  metadata: EventMetadata
): void;

/**
 * Appends a LinkTo event to a stream. Can only be used within the handler of {@link when}.
 *
 * @param streamId - Specifies the stream to which the LinkTo event will be emitted.
 * @param event - Event which will be linked.
 * @param metadata - A JavaScript object representing the JSON metadata of the LinkTo event.
 */
declare function linkTo(
  streamId: string,
  event: KurrentEvent,
  metadata: EventMetadata
): void;

/**
 * Appends a StreamReference event to a stream. Can only be used within the handler of {@link when}.
 *
 * @param streamId - Specifies the stream to which the StreamReference event will be emitted.
 * @param linkedStreamId - Specifies the stream to be linked.
 */
declare function linkStreamTo(streamId: string, linkedStreamId: string): void;
