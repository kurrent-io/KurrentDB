// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Core.Tests.Services.GossipService;

class gossip_service_should {
	//[Test]
	//public void UpdateListOfKnownBoxesAndSendMergedInformationBackWhenRequestForKnownBoxesReceived()
	//{
	//    var now = DateTime.UtcNow;

	//    var boxAOld = new BoxInfo("box_A", now.AddMinutes(-5));
	//    var boxANew = new BoxInfo("box_A", now.AddMinutes(-1));

	//    var boxB = new BoxInfo("box_B", now.AddMinutes(-7));
	//    var boxC = new BoxInfo("box_C", now.AddMinutes(-2));

	//    var boxDOld = new BoxInfo("box_D", now.AddMinutes(-10));
	//    var boxDNew = new BoxInfo("box_D", now.AddMinutes(-3));

	//    var boxE = new BoxInfo("box_E", now.AddMinutes(-2));

	//    var initialKnownBoxes = new List<BoxInfo> { boxAOld, boxDNew, boxE };
	//    Announcements.BoxData.KnownBoxes.AddRange(initialKnownBoxes);

	//    var externalNodeKnownBoxes = new List<BoxInfo> { boxANew, boxB, boxC, boxDOld };
	//    Publish(new BoxMessage.BoxesInformationRequest(externalNodeKnownBoxes));

	//    var genericMessage = BoxMessenger.Messages.Last();
	//    var responseWithUpdatedListofKnownBoxes = (BoxMessage.BoxesInformationResponse)genericMessage;

	//    var expectedBoxes = new List<BoxInfo> { boxANew, boxB, boxC, boxDNew, boxE };
	//    CollectionAssert.AreEquivalent(expectedBoxes, responseWithUpdatedListofKnownBoxes.Boxes);
	//}

	//[Test]
	//public void UpdateListOfKnownBoxesWhenResponseForAnnouncementReceived()
	//{
	//    var now = DateTime.UtcNow;

	//    var boxAOld = new BoxInfo("box_A", now.AddMinutes(-5));
	//    var boxANew = new BoxInfo("box_A", now.AddMinutes(-1));

	//    var boxB = new BoxInfo("box_B", now.AddMinutes(-7));
	//    var boxC = new BoxInfo("box_C", now.AddMinutes(-2));

	//    var boxDOld = new BoxInfo("box_D", now.AddMinutes(-10));
	//    var boxDNew = new BoxInfo("box_D", now.AddMinutes(-3));

	//    var boxE = new BoxInfo("box_E", now.AddMinutes(-2));


	//    var initialKnownBoxes = new List<BoxInfo> { boxAOld, boxDNew, boxE };
	//    Announcements.BoxData.KnownBoxes.AddRange(initialKnownBoxes);

	//    var externalNodeKnownBoxes = new List<BoxInfo> { boxANew, boxB, boxC, boxDOld };
	//    Publish(new BoxMessage.BoxesInformationRequest(externalNodeKnownBoxes));

	//    var expectedBoxes = new List<BoxInfo> { boxANew, boxB, boxC, boxDNew, boxE };

	//    CollectionAssert.AreEquivalent(expectedBoxes, Announcements.BoxData.KnownBoxes);
	//}

	//[Test]
	//public void UpdateListOfKnownBoxesWhenBoxesInformationResponseMessageReceived()
	//{
	//    var currentlyKnownBoxes = new List<BoxInfo>
	//                                  {
	//                                      new BoxInfo("box_A", DateTime.UtcNow.AddMinutes(-5)),
	//                                      new BoxInfo("box_B", DateTime.UtcNow.AddMinutes(-7))
	//                                  };
	//    var receivedKnownBoxes = new List<BoxInfo>
	//                                  {
	//                                      new BoxInfo("box_A", DateTime.UtcNow.AddMinutes(-1)),
	//                                      new BoxInfo("box_B", DateTime.UtcNow.AddMinutes(-3)),
	//                                      new BoxInfo("box_C", DateTime.UtcNow.AddMinutes(-5))
	//                                  };

	//    Announcements.BoxData.KnownBoxes.AddRange(currentlyKnownBoxes);

	//    Publish(new BoxMessage.BoxesInformationResponse(receivedKnownBoxes));

	//    CollectionAssert.AreEquivalent(receivedKnownBoxes, Announcements.BoxData.KnownBoxes);
	//}
}
