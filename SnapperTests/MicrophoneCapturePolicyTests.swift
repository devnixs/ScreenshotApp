import Testing
@testable import Snapper

struct MicrophoneCapturePolicyTests {
    @Test func fullDecisionTable() {
        var enabledCount = 0
        for setting in [false, true] {
            for authorization in MicrophoneAuthorization.allCases {
                for hasDevice in [false, true] {
                    let inputs = MicrophoneCaptureInputs(recordMicrophoneSetting: setting,
                        authorization: authorization, hasAudioInputDevice: hasDevice)
                    let captures = MicrophoneCapturePolicy.shouldCaptureMicrophone(inputs)
                    #expect(captures == (setting && authorization == .authorized && hasDevice))
                    if captures { enabledCount += 1 }
                }
            }
        }
        #expect(enabledCount == 1)
    }

    @Test func testSettingOffNeverCaptures() {
        #expect(!MicrophoneCapturePolicy.shouldCaptureMicrophone(.init(
            recordMicrophoneSetting: false, authorization: .authorized, hasAudioInputDevice: true)))
    }

    @Test func testRevokedPermissionRecordsVideoOnly() {
        #expect(!MicrophoneCapturePolicy.shouldCaptureMicrophone(.init(
            recordMicrophoneSetting: true, authorization: .denied, hasAudioInputDevice: true)))
    }

    @Test func testNotDeterminedDoesNotCapture() {
        #expect(!MicrophoneCapturePolicy.shouldCaptureMicrophone(.init(
            recordMicrophoneSetting: true, authorization: .notDetermined, hasAudioInputDevice: true)))
    }

    @Test func testNoInputDeviceDoesNotCapture() {
        #expect(!MicrophoneCapturePolicy.shouldCaptureMicrophone(.init(
            recordMicrophoneSetting: true, authorization: .authorized, hasAudioInputDevice: false)))
    }
}
